// Channel activity/health classification for the dashboard's channel list.
// Pure — no DOM, no chrome.* — runnable under `node --test channelHealth.test.js`.
// Loaded by a plain <script> in dashboard.html (dashboard.js is an ES module and
// can't import a non-module without wiring); publishes window.YSTHealth.

(function (root) {
  const DAY_MS = 24 * 60 * 60 * 1000;
  const ACTIVE_MAX_DAYS = 30;
  const QUIET_MAX_DAYS = 180;

  // videos: array of VideoInfo ({ publishedAt, ... }). Returns the newest
  // publishedAt as an ISO string, or null when there are no usable dates.
  function lastUploadAt(videos) {
    let newest = null;
    for (const v of videos || []) {
      const t = new Date(v.publishedAt).getTime();
      if (!Number.isNaN(t) && (newest === null || t > newest)) newest = t;
    }
    return newest === null ? null : new Date(newest).toISOString();
  }

  // { dead, videos } -> { status, lastUploadAt }
  //   status: "dead"    — flagged unreachable (removed / suspended)
  //           "active"  — uploaded within ACTIVE_MAX_DAYS
  //           "quiet"   — uploaded within QUIET_MAX_DAYS
  //           "dormant" — last upload older than that
  //           "unknown" — no cached uploads to judge from
  function classifyChannel({ dead = false, videos = [] } = {}, now = Date.now()) {
    if (dead) return { status: "dead", lastUploadAt: null };

    const last = lastUploadAt(videos);
    if (last === null) return { status: "unknown", lastUploadAt: null };

    const ageDays = (now - new Date(last).getTime()) / DAY_MS;
    const status =
      ageDays <= ACTIVE_MAX_DAYS ? "active" : ageDays <= QUIET_MAX_DAYS ? "quiet" : "dormant";
    return { status, lastUploadAt: last };
  }

  // Coarse "3 days ago" / "8 months ago" / "2 years ago". null in -> null out.
  function relativeTime(iso, now = Date.now()) {
    if (!iso) return null;
    const diffMs = now - new Date(iso).getTime();
    if (Number.isNaN(diffMs)) return null;

    const units = [
      ["year", 365 * DAY_MS],
      ["month", 30 * DAY_MS],
      ["week", 7 * DAY_MS],
      ["day", DAY_MS],
      ["hour", 60 * 60 * 1000],
    ];
    for (const [name, ms] of units) {
      const n = Math.floor(diffMs / ms);
      if (n >= 1) return `${n} ${name}${n === 1 ? "" : "s"} ago`;
    }
    return "just now";
  }

  // Sort key for the channel list — lower sorts first (needs more attention).
  const STATUS_ORDER = { dead: 0, dormant: 1, quiet: 2, active: 3, unknown: 4 };

  const api = { classifyChannel, relativeTime, lastUploadAt, STATUS_ORDER };
  if (typeof module !== "undefined" && module.exports) module.exports = api; // node test
  root.YSTHealth = api;
})(typeof window !== "undefined" ? window : globalThis);
