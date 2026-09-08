// Which brand-new videos to raise a desktop notification for, per group.
// Background-only, so this is an ES module (like storage.js) — the MV3 module
// service worker can't load the IIFE modules (feedFilter.js etc.).
//
// Pure: no chrome.*, no DOM. Test: node --test groupNotify.test.mjs

// Mirrors feedFilter.js `matchesAnyKeyword` — duplicated because that file is a
// browser IIFE this ES module can't import. Keep the two in sync.
function matchesBlockedKeyword(video, keywords, channelTitleOf) {
  if (!keywords || !keywords.length) return false;
  const haystack = `${video.title} ${channelTitleOf(video.channelId) || ""}`.toLowerCase();
  return keywords.some((k) => k && haystack.includes(k.toLowerCase()));
}

// groups: the stored groups map. oldByChannel / newByChannel: videosCache
// before and after this sync ({ channelId: [video, ...] }). A video counts as
// "new" for a notification only if it wasn't in the old cache (so a video that
// merely scrolled into our fetch window doesn't ping), was published after the
// previous sync, and would actually show in the feed (not watched / not
// interested / not blocked / channel not muted).
//
// Returns { [groupId]: [video, ...] } for groups with `notify` set that have at
// least one such video. `since == null` (no previous sync) yields nothing.
export function collectNewVideosForNotify(groups, oldByChannel, newByChannel, opts = {}) {
  const {
    since = null,
    watchedIds = new Set(),
    notInterestedIds = new Set(),
    mutedChannelIds = new Set(),
    blockedKeywords = [],
    channelTitleOf = () => "",
    now = Date.now(),
  } = opts;

  const result = {};
  if (since == null) return result;

  for (const [groupId, group] of Object.entries(groups || {})) {
    if (!group || !group.notify) continue;

    const seen = new Set();
    const fresh = [];
    for (const channelId of group.channelIds ?? []) {
      if (mutedChannelIds.has(channelId)) continue;
      const knownIds = new Set((oldByChannel[channelId] ?? []).map((v) => v.videoId));
      for (const video of newByChannel[channelId] ?? []) {
        if (seen.has(video.videoId) || knownIds.has(video.videoId)) continue;
        seen.add(video.videoId);
        const ts = new Date(video.publishedAt).getTime();
        if (!(ts > since) || ts > now) continue;
        if (watchedIds.has(video.videoId) || notInterestedIds.has(video.videoId)) continue;
        if (matchesBlockedKeyword(video, blockedKeywords, channelTitleOf)) continue;
        fresh.push(video);
      }
    }
    if (fresh.length) result[groupId] = fresh;
  }
  return result;
}
