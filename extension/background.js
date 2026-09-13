import * as api from "./youtubeApi.js";
import * as store from "./storage.js";
import { collectNewVideosForNotify } from "./groupNotify.js";
import { logger } from "./logger.js";

const DASHBOARD_URL = chrome.runtime.getURL("dashboard.html");
const GROUP_NOTIFY_PREFIX = "yst-group:";
const REFRESH_ALARM = "refresh-subscriptions";
const VIDEOS_PER_CHANNEL = 15;

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

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === REFRESH_ALARM) {
    refreshAll().catch((err) => logger.error(`Scheduled sync failed: ${err.message}`));
  }
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
      const cache = await store.getSubscriptionsCache();
      delete cache[message.channelId];
      await store.saveSubscriptionsCache(cache);
      return {};
    }
    default:
      throw new Error(`Unknown message type: ${message.type}`);
  }
}

// Pulls the current subscription list, then refreshes channel details and
// recent uploads for each one. Channels absent from the channels.list
// response are flagged dead rather than dropped, so the dashboard can offer
// to unsubscribe from them.
async function refreshAll() {
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
    const details = channelDetails.get(sub.channelId);
    subscriptionsCache[sub.channelId] = {
      subscriptionId: sub.subscriptionId,
      title: sub.title,
      thumbnail: sub.thumbnail,
      country: details?.country ?? null,
      uploadsPlaylistId: details?.uploadsPlaylistId ?? null,
      dead: !details,
    };
  }
  await store.saveSubscriptionsCache(subscriptionsCache);

  const videosCache = {};
  let failedChannels = 0;
  for (const [channelId, sub] of Object.entries(subscriptionsCache)) {
    if (sub.dead || !sub.uploadsPlaylistId) continue;
    try {
      const videoIds = await api.fetchRecentUploadIds(token, sub.uploadsPlaylistId, VIDEOS_PER_CHANNEL);
      if (videoIds.length === 0) continue;
      videosCache[channelId] = await api.fetchVideosDetails(token, videoIds);
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
  logger.info(
    `Sync OK — ${Object.keys(videosCache).length} channels, ${withNew} with new videos${suffix}`
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
