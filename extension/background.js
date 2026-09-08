import * as api from "./youtubeApi.js";
import * as store from "./storage.js";

const DASHBOARD_URL = chrome.runtime.getURL("dashboard.html");
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

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(REFRESH_ALARM, { periodInMinutes: 45 });
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === REFRESH_ALARM) refreshAll().catch(console.error);
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  handleMessage(message).then(sendResponse, (err) => {
    // Surface the failure — the caller only sees {error}, and a content-script
    // requestCountry() swallows it silently.
    console.error("[YST] handleMessage failed for", message?.type, err);
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
      if (token) await refreshAll();
      return { token };
    }
    case "SIGN_OUT":
      await api.signOut();
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
  for (const [channelId, sub] of Object.entries(subscriptionsCache)) {
    if (sub.dead || !sub.uploadsPlaylistId) continue;
    const videoIds = await api.fetchRecentUploadIds(token, sub.uploadsPlaylistId, VIDEOS_PER_CHANNEL);
    if (videoIds.length === 0) continue;
    const videos = await api.fetchVideosDetails(token, videoIds);
    videosCache[channelId] = videos;
  }
  await store.saveVideosCache(videosCache);

  // Liked videos — a bonus signal for the feed ("liked = watched"). A failure
  // here must not fail the whole sync.
  try {
    await store.saveLikedVideos(await api.fetchLikedVideos(token));
  } catch (err) {
    console.error("[YST] fetchLikedVideos failed", err);
  }

  await store.markSynced();
}
