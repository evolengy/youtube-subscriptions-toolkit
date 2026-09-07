// Storage schema and helpers.
// Sync storage: user settings the person actually curated (groups, watched list).
// Local storage: everything re-fetchable from the API (subscription/video cache).

const SYNC_KEYS = {
  groups: "groups",
  watchedVideoIds: "watchedVideoIds",
  notInterestedVideoIds: "notInterestedVideoIds",
};
const LOCAL_KEYS = {
  subscriptionsCache: "subscriptionsCache",
  videosCache: "videosCache",
  lastSyncedAt: "lastSyncedAt",
};

// chrome.storage.sync has an 8KB-per-item / ~100KB-total quota, so the watched
// list is capped instead of growing forever.
const MAX_WATCHED_IDS = 2000;

export async function getGroups() {
  const { [SYNC_KEYS.groups]: groups } = await chrome.storage.sync.get(SYNC_KEYS.groups);
  return groups || {};
}

export async function saveGroups(groups) {
  await chrome.storage.sync.set({ [SYNC_KEYS.groups]: groups });
}

export async function upsertGroup(groupId, name, channelIds) {
  const groups = await getGroups();
  // Spread the existing entry so other fields (icon) survive a name/channel edit.
  groups[groupId] = { ...groups[groupId], name, channelIds };
  await saveGroups(groups);
  return groups;
}

export async function setGroupIcon(groupId, icon) {
  const groups = await getGroups();
  if (!groups[groupId]) return groups;
  if (icon) groups[groupId].icon = icon;
  else delete groups[groupId].icon;
  await saveGroups(groups);
  return groups;
}

export async function deleteGroup(groupId) {
  const groups = await getGroups();
  delete groups[groupId];
  await saveGroups(groups);
  return groups;
}

export async function getWatchedVideoIds() {
  const { [SYNC_KEYS.watchedVideoIds]: ids } = await chrome.storage.sync.get(
    SYNC_KEYS.watchedVideoIds
  );
  return new Set(ids || []);
}

export async function markVideoWatched(videoId) {
  const ids = await getWatchedVideoIds();
  ids.add(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ [SYNC_KEYS.watchedVideoIds]: trimmed });
}

// "Not interested" — an extension-only list (YouTube has no API for its own).
// Hidden from the feed by default; capped like the watched list.
export async function getNotInterestedVideoIds() {
  const { [SYNC_KEYS.notInterestedVideoIds]: ids } = await chrome.storage.sync.get(
    SYNC_KEYS.notInterestedVideoIds
  );
  return new Set(ids || []);
}

export async function addNotInterested(videoId) {
  const ids = await getNotInterestedVideoIds();
  ids.add(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ [SYNC_KEYS.notInterestedVideoIds]: trimmed });
}

export async function removeNotInterested(videoId) {
  const ids = await getNotInterestedVideoIds();
  if (!ids.delete(videoId)) return;
  await chrome.storage.sync.set({ [SYNC_KEYS.notInterestedVideoIds]: Array.from(ids) });
}

export async function getSubscriptionsCache() {
  const { [LOCAL_KEYS.subscriptionsCache]: cache } = await chrome.storage.local.get(
    LOCAL_KEYS.subscriptionsCache
  );
  return cache || {};
}

export async function saveSubscriptionsCache(cache) {
  await chrome.storage.local.set({ [LOCAL_KEYS.subscriptionsCache]: cache });
}

export async function getVideosCache() {
  const { [LOCAL_KEYS.videosCache]: cache } = await chrome.storage.local.get(
    LOCAL_KEYS.videosCache
  );
  return cache || {};
}

export async function saveVideosCache(cache) {
  await chrome.storage.local.set({ [LOCAL_KEYS.videosCache]: cache });
}

export async function setLastSyncedAt(timestamp) {
  await chrome.storage.local.set({ [LOCAL_KEYS.lastSyncedAt]: timestamp });
}

export async function getLastSyncedAt() {
  const { [LOCAL_KEYS.lastSyncedAt]: ts } = await chrome.storage.local.get(
    LOCAL_KEYS.lastSyncedAt
  );
  return ts || null;
}
