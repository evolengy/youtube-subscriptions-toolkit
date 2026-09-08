// "N new since you last looked" counts for the group list, shared by the
// dashboard (ES module) and the in-page sidebar (content script) — same
// plain-script-publishes-a-global shape as feedFilter.js, listed before the
// consumer in the manifest / page.
//
// Pure: no DOM, no chrome.* — run under `node --test groupCounts.test.js`.

(function (root) {
  const ALL_KEY = "__all__";

  // Count videos across `channelIds` published after `since` (a ms timestamp),
  // excluding ones already watched or marked not-interested. `since == null`
  // (group never opened) yields 0 — no badge until there's a baseline. Future-
  // dated entries (ts > now) are ignored as clock junk.
  function countNew(channelIds, videosByChannel, since, watchedIds, notInterestedIds, now) {
    if (since == null) return 0;
    let n = 0;
    for (const channelId of channelIds) {
      for (const video of videosByChannel[channelId] ?? []) {
        const ts = new Date(video.publishedAt).getTime();
        if (
          ts > since &&
          ts <= now &&
          !watchedIds.has(video.videoId) &&
          !notInterestedIds.has(video.videoId)
        ) {
          n++;
        }
      }
    }
    return n;
  }

  // -> { [groupId]: count, __all__: count }. Every group gets an entry (0
  // included); the caller renders a badge only when count > 0.
  //
  // A group with no lastVisited entry (never opened) falls back to
  // `fallbackSince` — the caller passes the previous sync's timestamp, so a
  // fresh install still shows "arrived in the last sync" rather than nothing.
  // With neither, the count is 0.
  function countNewPerGroup(groups, videosByChannel, opts = {}) {
    const {
      lastVisited = {},
      fallbackSince = null,
      watchedIds = new Set(),
      notInterestedIds = new Set(),
      allChannelIds = [],
      now = Date.now(),
    } = opts;

    const since = (key) => lastVisited[key] ?? fallbackSince;

    const result = {};
    for (const [groupId, group] of Object.entries(groups || {})) {
      result[groupId] = countNew(
        group.channelIds ?? [],
        videosByChannel,
        since(groupId),
        watchedIds,
        notInterestedIds,
        now
      );
    }
    result[ALL_KEY] = countNew(
      allChannelIds,
      videosByChannel,
      since(ALL_KEY),
      watchedIds,
      notInterestedIds,
      now
    );
    return result;
  }

  const api = { countNewPerGroup, ALL_KEY };

  if (typeof module !== "undefined" && module.exports) module.exports = api; // node test
  root.YSTGroupCounts = api; // browser (dashboard + content script)
})(typeof window !== "undefined" ? window : globalThis);
