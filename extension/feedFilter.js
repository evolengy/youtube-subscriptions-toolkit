// Shared feed filtering/sorting, used by both the dashboard (dashboard.js, an
// ES module) and the in-page overlay (content-groups.js, a plain content
// script). Content scripts can't import an ES module without extra wiring, so
// this is a plain script that publishes a global; both surfaces load it first.
//
// Everything here is pure — no DOM, no chrome.* — so it can be run under
// `node --test feedFilter.test.mjs`.

(function (root) {
  const SHORT_MAX_SECONDS = 60;

  function parseIsoDuration(iso) {
    const match = /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso ?? "");
    if (!match) return 0;
    const [, h, m, s] = match;
    return (Number(h) || 0) * 3600 + (Number(m) || 0) * 60 + (Number(s) || 0);
  }

  // Live/upcoming status is authoritative and checked first — a livestream can
  // report a near-zero or stale duration while it's airing, which would
  // otherwise be misread as a Short. An ended stream reports
  // liveBroadcastContent "none" and falls through to the duration check.
  function classifyVideoType(video, durationSeconds) {
    if (video.liveBroadcastContent === "live" || video.liveBroadcastContent === "upcoming") {
      return "live";
    }
    return durationSeconds <= SHORT_MAX_SECONDS ? "short" : "video";
  }

  // Duration buckets. "short" here is the UI bucket (< 4 min), unrelated to the
  // Shorts video type.
  function matchesDuration(seconds, bucket) {
    if (bucket === "under4") return seconds <= 240;
    if (bucket === "4to20") return seconds > 240 && seconds <= 1200;
    if (bucket === "over20") return seconds > 1200;
    return true; // "any" / unknown
  }

  const UPLOAD_WINDOWS_MS = {
    today: 24 * 60 * 60 * 1000,
    week: 7 * 24 * 60 * 60 * 1000,
    month: 30 * 24 * 60 * 60 * 1000,
  };

  function matchesUploaded(publishedAt, bucket, now) {
    const windowMs = UPLOAD_WINDOWS_MS[bucket];
    if (!windowMs) return true; // "any" / unknown
    return now - new Date(publishedAt).getTime() <= windowMs;
  }

  // Case-insensitive substring match against the video title and the channel
  // name together, so "prime tutorial" won't match (it's a single substring
  // check) but "prime" matches either field. applyFilters only calls this when
  // `query` is non-blank.
  function matchesQuery(video, query, channelTitleOf) {
    const needle = query.toLowerCase();
    const haystack = `${video.title} ${channelTitleOf(video.channelId) || ""}`.toLowerCase();
    return haystack.includes(needle);
  }

  // videos: raw VideoInfo objects (with .duration ISO string, .publishedAt,
  // .viewCount, .liveBroadcastContent, .videoId, .channelId, .title).
  // Returns a new array, enriched with { durationSeconds, type }, filtered and
  // sorted. Callers render straight from the result.
  function applyFilters(videos, opts) {
    const {
      type = "all",
      sortBy = "date",
      hideWatched = false,
      watchedIds = new Set(),
      duration = "any",
      uploadedWithin = "any",
      query = "",
      channelTitleOf = () => "",
      notInterestedIds = new Set(),
      showNotInterested = false,
      likedIds = new Set(),
      likedAsWatched = false,
    } = opts || {};

    const now = Date.now();
    const trimmedQuery = query.trim();

    const enriched = videos.map((v) => {
      const durationSeconds = parseIsoDuration(v.duration);
      const liked = likedIds.has(v.videoId);
      return { ...v, durationSeconds, type: classifyVideoType(v, durationSeconds), liked };
    });

    const isWatched = (v) => watchedIds.has(v.videoId) || (likedAsWatched && v.liked);

    const filtered = enriched.filter((v) => {
      // "Not interested" is hidden by default; showNotInterested surfaces them
      // (so they can be restored).
      if (notInterestedIds.has(v.videoId) && !showNotInterested) return false;
      if (type !== "all" && v.type !== type) return false;
      if (hideWatched && isWatched(v)) return false;
      if (!matchesDuration(v.durationSeconds, duration)) return false;
      if (!matchesUploaded(v.publishedAt, uploadedWithin, now)) return false;
      if (trimmedQuery && !matchesQuery(v, trimmedQuery, channelTitleOf)) return false;
      return true;
    });

    filtered.sort((a, b) => {
      if (sortBy === "duration") return b.durationSeconds - a.durationSeconds;
      if (sortBy === "views") return b.viewCount - a.viewCount;
      return new Date(b.publishedAt) - new Date(a.publishedAt);
    });

    return filtered;
  }

  // Shape a stored id list (liked videos, "not interested") into feed-card
  // objects applyFilters/renderers can consume. Liked and not-interested are
  // persisted trimmed — { videoId, title, thumbnail, channelId, publishedAt } —
  // so per id we merge: the recent-uploads cache entry (full: duration,
  // viewCount, live status) underneath, the stored meta on top, then defaults
  // for anything still missing. An id with neither becomes a bare placeholder
  // (title = the id, a working /watch link) rather than being dropped.
  function hydrateVideoList(ids, metaMap = {}, fallbackVideos = []) {
    const byId = new Map(fallbackVideos.map((v) => [v.videoId, v]));
    return ids.map((id) => {
      const src = { ...(byId.get(id) || {}), ...(metaMap[id] || {}) };
      return {
        videoId: id,
        title: src.title ?? id,
        thumbnail: src.thumbnail ?? null,
        channelId: src.channelId ?? null,
        publishedAt: src.publishedAt ?? null,
        duration: src.duration ?? null,
        viewCount: src.viewCount ?? 0,
        liveBroadcastContent: src.liveBroadcastContent ?? "none",
      };
    });
  }

  const api = { parseIsoDuration, classifyVideoType, matchesQuery, applyFilters, hydrateVideoList };

  if (typeof module !== "undefined" && module.exports) module.exports = api; // node test
  root.YSTFeed = api; // browser (dashboard + content script)
})(typeof window !== "undefined" ? window : globalThis);
