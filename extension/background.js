import * as api from "./youtubeApi.js";
import * as store from "./storage.js";
import { collectNewVideosForNotify } from "./groupNotify.js";
import { logger } from "./logger.js";
import {
  SUBSCRIPTION_REQUEST_URLS,
  decodeRawBody,
  parseSubscriptionRequest,
  diffSubscriptions,
  removeChannels,
} from "./subscriptionWatch.js";

const DASHBOARD_URL = chrome.runtime.getURL("dashboard.html");
const GROUP_NOTIFY_PREFIX = "yst-group:";
const REFRESH_ALARM = "refresh-subscriptions";
// Cheap list-only check (ceil(N/50) units) — catches subscribe/unsubscribe made
// outside this browser (phone, another PC) between full syncs.
const SUBSCRIPTIONS_ALARM = "check-subscriptions";
const SUBSCRIPTIONS_CHECK_MINUTES = 10;
const VIDEOS_PER_CHANNEL = 15;
// After a subscribe click the Data API can lag the write, so
// subscriptions.list?forChannelId= is retried after these waits (ms).
const SUBSCRIBE_LOOKUP_DELAYS = [2000, 5000, 10000];

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Every operation that rewrites subscriptionsCache / videosCache runs through
// this queue. A full refreshAll() replaces the whole cache from its own
// snapshot, so a quick subscribe/unsubscribe update landing mid-run would
// otherwise be silently overwritten. Never call exclusive() from inside an
// exclusive() callback — it would wait on itself.
let cacheQueue = Promise.resolve();
function exclusive(fn) {
  const run = cacheQueue.then(fn, fn);
  cacheQueue = run.catch(() => {});
  return run;
}

async function openOrFocusDashboard() {
  const [existing] = await chrome.tabs.query({ url: DASHBOARD_URL });
  if (existing) {
    await chrome.tabs.update(existing.id, { active: true });
    await chrome.windows.update(existing.windowId, { focused: true });
  } else {
    await chrome.tabs.create({ url: DASHBOARD_URL });
  }
}

chrome.action.onClicked.addListener(openOrFocusDashboard);

// A "new videos in <group>" notification opens the dashboard filtered to it.
chrome.notifications.onClicked.addListener((notificationId) => {
  if (!notificationId.startsWith(GROUP_NOTIFY_PREFIX)) return;
  const groupId = notificationId.slice(GROUP_NOTIFY_PREFIX.length);
  chrome.tabs.create({ url: `${DASHBOARD_URL}#group=${encodeURIComponent(groupId)}` });
  chrome.notifications.clear(notificationId);
});

// (Re)creates the alarm at the stored interval — chrome.alarms.create with an
// existing name replaces it, so this is also how a settings.html edit takes
// effect immediately, without reloading the extension.
async function ensureAlarm() {
  const periodInMinutes = await store.getSyncIntervalMinutes();
  chrome.alarms.create(REFRESH_ALARM, { periodInMinutes });
  chrome.alarms.create(SUBSCRIPTIONS_ALARM, { periodInMinutes: SUBSCRIPTIONS_CHECK_MINUTES });
  // A visible trail in logs.html that this actually ran, and at what period —
  // otherwise there's no way to tell "never scheduled" from "scheduled but the
  // browser wasn't open when it was due" (chrome.alarms doesn't backfill).
  logger.info(`Background sync (re)scheduled — every ${periodInMinutes} min`);
}

chrome.runtime.onInstalled.addListener(() => {
  ensureAlarm().catch((err) => logger.error(`Could not schedule sync on install: ${err.message}`));
});

// onInstalled only fires on install/update — a plain browser restart needs its
// own hook, or a stored interval changed while the browser was closed would
// only apply after the next update.
chrome.runtime.onStartup.addListener(() => {
  ensureAlarm().catch((err) => logger.error(`Could not schedule sync on startup: ${err.message}`));
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.syncIntervalMinutes) {
    ensureAlarm().catch((err) => logger.error(`Could not reschedule sync: ${err.message}`));
  }
});

chrome.alarms.onAlarm.addListener(async (alarm) => {
  if (alarm.name === SUBSCRIPTIONS_ALARM) {
    await syncSubscriptionList().catch((err) =>
      logger.error(`Subscription check failed: ${err.message}`)
    );
    return;
  }
  if (alarm.name !== REFRESH_ALARM) return;
  // Logged (and awaited) before refreshAll() starts, deliberately separate from
  // "Sync OK" / any error it might log — if this line is missing after a wait,
  // the alarm never reached this listener at all; if it's here but nothing
  // else ever follows, refreshAll() (or the service worker itself) died
  // mid-run without a chance to log why.
  await logger.info("Scheduled sync alarm fired");
  await refreshAll().catch((err) => logger.error(`Scheduled sync failed: ${err.message}`));
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  handleMessage(message).then(sendResponse, (err) => {
    // Surface the failure — the caller only sees {error}, and a content-script
    // requestCountry() swallows it silently.
    logger.error(`${message?.type} failed: ${err.message}`);
    sendResponse({ error: err.message });
  });
  return true; // keep the message channel open for the async response
});

async function handleMessage(message) {
  switch (message.type) {
    case "OPEN_DASHBOARD":
      await openOrFocusDashboard();
      return {};
    case "SIGN_IN": {
      const token = await api.getAuthToken({ interactive: true });
      if (token) {
        logger.info("Signed in");
        await refreshAll();
      }
      return { token };
    }
    case "SIGN_OUT":
      await api.signOut();
      logger.info("Signed out");
      return {};
    case "GET_AUTH_STATUS": {
      const token = await api.getAuthToken({ interactive: false });
      return { signedIn: Boolean(token) };
    }
    case "REFRESH_ALL":
      await refreshAll();
      return {};
    case "GET_CHANNEL_COUNTRY": {
      const token = await api.getAuthToken({ interactive: false });
      if (!token) return { country: null };
      const detail = await api.fetchChannelDetail(token, {
        handle: message.handle,
        channelId: message.channelId,
      });
      return { country: detail?.country ?? null };
    }
    case "UNSUBSCRIBE": {
      const token = await api.getAuthToken({ interactive: false });
      await api.unsubscribe(token, message.subscriptionId);
      await exclusive(() => dropChannels([message.channelId]));
      return {};
    }
    case "SYNC_SUBSCRIPTIONS":
      await syncSubscriptionList();
      return {};
    default:
      throw new Error(`Unknown message type: ${message.type}`);
  }
}

// MV3 service workers are suspended after ~30s with no extension-API
// activity, and a long chain of plain fetch() calls (refreshAll's
// per-channel loop below) doesn't reliably reset that timer — so a
// background-triggered sync can be killed mid-run with nothing to catch or
// log, while a manual "Refresh now" (page open, actively used) rarely hits
// it. A trivial chrome.* call on an interval well under 30s keeps the worker
// alive for the duration; the returned function stops it.
function keepServiceWorkerAlive(intervalMs = 15000) {
  const id = setInterval(() => {
    chrome.storage.local.get("keepAlivePing");
  }, intervalMs);
  return () => clearInterval(id);
}

// Pulls the current subscription list, then refreshes channel details and
// recent uploads for each one. Channels absent from the channels.list
// response are flagged dead rather than dropped, so the dashboard can offer
// to unsubscribe from them.
async function refreshAll() {
  const stopKeepAlive = keepServiceWorkerAlive();
  try {
    await exclusive(refreshAllInner);
  } finally {
    stopKeepAlive();
  }
}

async function refreshAllInner() {
  const startedAt = Date.now();
  const token = await api.getAuthToken({ interactive: false });
  if (!token) return;

  // Captured before we overwrite the caches — used to spot genuinely new videos.
  const previousVideosCache = await store.getVideosCache();
  const notifySince = await store.getLastSyncedAt();

  const subscriptions = await api.fetchAllSubscriptions(token);
  const channelIds = subscriptions.map((s) => s.channelId);
  const channelDetails = await api.fetchChannelsDetails(token, channelIds);

  const subscriptionsCache = {};
  for (const sub of subscriptions) {
    subscriptionsCache[sub.channelId] = toCacheEntry(sub, channelDetails.get(sub.channelId));
  }
  // Unsubscribed elsewhere since the last sync — prune them from groups, same
  // as the quick paths below do (skipped on the first-ever sync / empty answer).
  const previousSubscriptions = await store.getSubscriptionsCache();
  if (notifySince && subscriptions.length) {
    const { removed } = diffSubscriptions(previousSubscriptions, subscriptions);
    const pruned = removeChannels({ groups: await store.getGroups() }, removed);
    if (pruned.groupsChanged) await store.saveGroups(pruned.groups);
  }
  await store.saveSubscriptionsCache(subscriptionsCache);

  const videosCache = {};
  let failedChannels = 0;
  for (const [channelId, sub] of Object.entries(subscriptionsCache)) {
    if (sub.dead || !sub.uploadsPlaylistId) continue;
    try {
      const videos = await fetchRecentVideos(token, sub.uploadsPlaylistId);
      if (videos.length === 0) continue;
      videosCache[channelId] = videos;
    } catch (err) {
      // One channel failing (transient API error that outlasted the retries,
      // or a per-channel 403) must not discard the whole sync. Keep whatever we
      // had for this channel and move on.
      failedChannels++;
      logger.error(`Refresh failed for ${sub.title || channelId}: ${err.message}`);
      if (previousVideosCache[channelId]) videosCache[channelId] = previousVideosCache[channelId];
    }
  }
  await store.saveVideosCache(videosCache);

  // "New videos in <group>" notifications. Isolated — a bug here must not fail
  // the sync.
  try {
    await notifyGroupsOfNewVideos(previousVideosCache, videosCache, notifySince);
  } catch (err) {
    logger.error(`Group notifications failed: ${err.message}`);
  }

  // Liked videos — a bonus signal for the feed ("liked = watched"). A failure
  // here must not fail the whole sync.
  try {
    await store.saveLikedVideos(await api.fetchLikedVideos(token));
  } catch (err) {
    logger.error(`Liked-videos sync failed: ${err.message}`);
  }

  await store.markSynced();

  const withNew = Object.entries(videosCache).filter(([cid, vids]) => {
    const known = new Set((previousVideosCache[cid] || []).map((v) => v.videoId));
    return vids.some((v) => !known.has(v.videoId));
  }).length;
  const suffix = failedChannels ? `, ${failedChannels} failed` : "";
  const seconds = ((Date.now() - startedAt) / 1000).toFixed(1);
  logger.info(
    `Sync OK — ${Object.keys(videosCache).length} channels, ${withNew} with new videos${suffix} (${seconds}s)`
  );
}

async function notifyGroupsOfNewVideos(previousVideosCache, videosCache, since) {
  const [groups, subscriptionsCache, watchedIds, notInterestedIds, blocklist] =
    await Promise.all([
      store.getGroups(),
      store.getSubscriptionsCache(),
      store.getWatchedVideoIds(),
      store.getNotInterestedVideoIds(),
      store.getFeedBlocklist(),
    ]);

  if (!since || !Object.values(groups).some((g) => g.notify)) return;

  const perGroup = collectNewVideosForNotify(groups, previousVideosCache, videosCache, {
    since,
    watchedIds,
    notInterestedIds,
    mutedChannelIds: new Set(blocklist.mutedChannels),
    blockedKeywords: blocklist.keywords,
    channelTitleOf: (id) => subscriptionsCache[id]?.title ?? "",
  });

  const iconUrl = chrome.runtime.getURL("icons/icon128.png");
  for (const [groupId, videos] of Object.entries(perGroup)) {
    const group = groups[groupId];
    const icon = group.icon || "📁";
    const count = videos.length;
    chrome.notifications.create(`${GROUP_NOTIFY_PREFIX}${groupId}`, {
      type: "basic",
      iconUrl,
      title: `${icon} ${group.name}: ${count} new video${count > 1 ? "s" : ""}`,
      message: videos.slice(0, 3).map((v) => v.title).join("\n"),
    });
  }
}

// --- Shared cache helpers --------------------------------------------------

// sub: a fetchAllSubscriptions / fetchSubscriptionFor entry; details: its
// channels.list result, or undefined when the channel is gone (→ dead).
function toCacheEntry(sub, details) {
  return {
    subscriptionId: sub.subscriptionId,
    title: sub.title,
    thumbnail: sub.thumbnail,
    country: details?.country ?? null,
    uploadsPlaylistId: details?.uploadsPlaylistId ?? null,
    dead: !details,
  };
}

async function fetchRecentVideos(token, uploadsPlaylistId) {
  const videoIds = await api.fetchRecentUploadIds(token, uploadsPlaylistId, VIDEOS_PER_CHANNEL);
  return videoIds.length ? api.fetchVideosDetails(token, videoIds) : [];
}

// Adds freshly subscribed channels to both caches: 1 channels.list call for the
// batch + playlistItems/videos per channel (~2 units each) — instead of a full
// refreshAll(). Call inside exclusive().
async function addChannels(token, subs) {
  if (!subs.length) return;
  const details = await api.fetchChannelsDetails(token, subs.map((s) => s.channelId));
  const [subscriptionsCache, videosCache] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getVideosCache(),
  ]);
  for (const sub of subs) {
    const entry = toCacheEntry(sub, details.get(sub.channelId));
    subscriptionsCache[sub.channelId] = entry;
    if (entry.dead || !entry.uploadsPlaylistId) continue;
    try {
      const videos = await fetchRecentVideos(token, entry.uploadsPlaylistId);
      if (videos.length) videosCache[sub.channelId] = videos;
    } catch (err) {
      // The channel is still subscribed — keep it; its videos come with the next full sync.
      logger.error(`Loading videos for ${sub.title || sub.channelId} failed: ${err.message}`);
    }
  }
  await store.saveSubscriptionsCache(subscriptionsCache);
  await store.saveVideosCache(videosCache);
}

// Unsubscribed channels leave both caches and every group. Call inside exclusive().
async function dropChannels(channelIds) {
  if (!channelIds.length) return;
  const [subscriptionsCache, videosCache, groups] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getVideosCache(),
    store.getGroups(),
  ]);
  const next = removeChannels({ subscriptionsCache, videosCache, groups }, channelIds);
  await store.saveSubscriptionsCache(next.subscriptionsCache);
  await store.saveVideosCache(next.videosCache);
  if (next.groupsChanged) await store.saveGroups(next.groups);
}

// --- Subscription list check -------------------------------------------------

// Diffs the live subscription list against the cache and applies only the
// difference. Costs ceil(N/50) units plus ~2 per newly added channel, so it
// can run far more often than refreshAll(). Before the first full sync there
// is nothing to diff against — that sync builds the cache itself.
async function syncSubscriptionList() {
  const token = await api.getAuthToken({ interactive: false });
  if (!token) return;
  const stopKeepAlive = keepServiceWorkerAlive();
  try {
    await exclusive(async () => {
      if (!(await store.getLastSyncedAt())) return;
      const cache = await store.getSubscriptionsCache();
      const fresh = await api.fetchAllSubscriptions(token);
      // An empty answer against a non-empty cache is far likelier an API hiccup
      // than "unsubscribed from everything" — and acting on it would strip every group.
      if (fresh.length === 0 && Object.keys(cache).length > 0) {
        logger.error("Subscription check got an empty list — ignored");
        return;
      }
      const { added, removed } = diffSubscriptions(cache, fresh);
      if (!added.length && !removed.length) return;
      await dropChannels(removed);
      await addChannels(token, added);
      logger.info(`Subscriptions changed: +${added.length} / −${removed.length}`);
    });
  } finally {
    stopKeepAlive();
  }
}

// --- Subscribe / unsubscribe clicks on youtube.com ---------------------------

// requestId → parsed request, between onBeforeRequest (has the body) and
// onCompleted (has the status). Lost if the worker restarts in between —
// onCompleted then falls back to the list check.
const pendingSubscriptionRequests = new Map();
const subscriptionFilter = { urls: SUBSCRIPTION_REQUEST_URLS };

chrome.webRequest.onBeforeRequest.addListener(
  (details) => {
    if (details.method !== "POST") return;
    const parsed = parseSubscriptionRequest(details.url, decodeRawBody(details.requestBody?.raw));
    if (parsed) pendingSubscriptionRequests.set(details.requestId, parsed);
  },
  subscriptionFilter,
  ["requestBody"]
);

chrome.webRequest.onCompleted.addListener((details) => {
  const parsed =
    pendingSubscriptionRequests.get(details.requestId) ??
    parseSubscriptionRequest(details.url, "");
  pendingSubscriptionRequests.delete(details.requestId);
  if (!parsed || details.method !== "POST" || details.statusCode !== 200) return;
  applyWebSubscriptionChange(parsed).catch((err) =>
    logger.error(`Applying youtube.com ${parsed.action} failed: ${err.message}`)
  );
}, subscriptionFilter);

chrome.webRequest.onErrorOccurred.addListener((details) => {
  pendingSubscriptionRequests.delete(details.requestId);
}, subscriptionFilter);

async function applyWebSubscriptionChange({ action, channelIds }) {
  const stopKeepAlive = keepServiceWorkerAlive();
  try {
    if (!channelIds.length) {
      // Body unreadable — we know *something* changed, not what. Give the API a
      // moment to catch up, then diff the whole list.
      await sleep(SUBSCRIBE_LOOKUP_DELAYS[0]);
      await syncSubscriptionList();
      return;
    }

    if (action === "unsubscribe") {
      await exclusive(() => dropChannels(channelIds));
      logger.info(`Unsubscribed on youtube.com: ${channelIds.join(", ")}`);
      return;
    }

    const token = await api.getAuthToken({ interactive: false });
    if (!token) return;
    const cache = await store.getSubscriptionsCache();
    const found = [];
    for (const channelId of channelIds) {
      if (cache[channelId]) continue;
      const sub = await lookUpNewSubscription(token, channelId);
      if (sub) found.push(sub);
      else logger.info(`Subscribed to ${channelId} on youtube.com — not visible in the API yet, the next check will pick it up`);
    }
    if (!found.length) return;
    await exclusive(() => addChannels(token, found));
    logger.info(`Subscribed on youtube.com: ${found.map((s) => s.title || s.channelId).join(", ")}`);
  } finally {
    stopKeepAlive();
  }
}

async function lookUpNewSubscription(token, channelId) {
  for (const delay of SUBSCRIBE_LOOKUP_DELAYS) {
    await sleep(delay);
    const sub = await api.fetchSubscriptionFor(token, channelId);
    if (sub) return sub;
  }
  return null;
}
