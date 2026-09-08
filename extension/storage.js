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
  likedVideos: "likedVideos",
  notInterestedVideos: "notInterestedVideos",
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
//
// Two stores: the id list lives in sync (small, portable, and the source of
// truth every feed render filters against), and a parallel metadata map lives
// in local (title/thumbnail/etc — only needed to render the standalone list,
// too big for the sync quota at 2000 entries). The map is kept in lockstep
// with the trimmed id list so it can't outgrow it.
export async function getNotInterestedVideoIds() {
  const { [SYNC_KEYS.notInterestedVideoIds]: ids } = await chrome.storage.sync.get(
    SYNC_KEYS.notInterestedVideoIds
  );
  return new Set(ids || []);
}

// Map { videoId: { videoId, title, thumbnail, channelId, publishedAt } }.
export async function getNotInterestedVideos() {
  const { [LOCAL_KEYS.notInterestedVideos]: map } = await chrome.storage.local.get(
    LOCAL_KEYS.notInterestedVideos
  );
  return map || {};
}

function pickVideoMeta(video) {
  return {
    videoId: video.videoId,
    title: video.title ?? null,
    thumbnail: video.thumbnail ?? null,
    channelId: video.channelId ?? null,
    publishedAt: video.publishedAt ?? null,
  };
}

// Drop map entries whose id is no longer in `keepIds` (e.g. trimmed off the cap).
function pruneMetaMap(map, keepIds) {
  const keep = new Set(keepIds);
  const out = {};
  for (const [id, value] of Object.entries(map)) if (keep.has(id)) out[id] = value;
  return out;
}

// Accepts a full video object (preferred — populates the metadata map) or a
// bare id (list-only, e.g. a legacy caller).
export async function addNotInterested(video) {
  const videoId = typeof video === "string" ? video : video.videoId;
  const ids = await getNotInterestedVideoIds();
  ids.add(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ [SYNC_KEYS.notInterestedVideoIds]: trimmed });

  const map = await getNotInterestedVideos();
  if (typeof video === "object" && video) map[videoId] = pickVideoMeta(video);
  await chrome.storage.local.set({
    [LOCAL_KEYS.notInterestedVideos]: pruneMetaMap(map, trimmed),
  });
}

export async function removeNotInterested(videoId) {
  const ids = await getNotInterestedVideoIds();
  if (!ids.delete(videoId)) return;
  await chrome.storage.sync.set({ [SYNC_KEYS.notInterestedVideoIds]: Array.from(ids) });

  const map = await getNotInterestedVideos();
  if (map[videoId]) {
    delete map[videoId];
    await chrome.storage.local.set({ [LOCAL_KEYS.notInterestedVideos]: map });
  }
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

// Map { videoId: { title, thumbnail, channelId, publishedAt } } — the recent
// slice of the user's "Liked videos" playlist, re-fetched each sync.
export async function getLikedVideos() {
  const { [LOCAL_KEYS.likedVideos]: liked } = await chrome.storage.local.get(
    LOCAL_KEYS.likedVideos
  );
  return liked || {};
}

export async function saveLikedVideos(list) {
  const map = {};
  for (const v of list) map[v.videoId] = v;
  await chrome.storage.local.set({ [LOCAL_KEYS.likedVideos]: map });
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
