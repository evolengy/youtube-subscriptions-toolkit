// Everything this extension injects into youtube.com itself:
//   - a "My groups" section in YouTube's own left sidebar,
//   - an overlay (docked below the masthead) rendering the grouped feed
//     without touching YouTube's actual feed DOM,
//   - the channel-country badge on watch pages (was content-location.js).
// Group/channel management stays in dashboard.html — the on-site surface is
// read/switch only, reusing masthead search and navigation underneath.
//
// Plain script (not an ES module) — content scripts can't import an ES module
// without extra wiring. Feed filtering/sorting/classification is shared with
// the dashboard via feedFilter.js (window.YSTFeed), listed before this file in
// the manifest so the global is ready.

const MAX_WATCHED_IDS = 2000;
const ALL_KEY = "__all__";
let activeGroupKey = null;

const { applyFilters } = window.YSTFeed;

// Content-script failures are otherwise silent — surface them with a tag.
const warn = (...a) => console.warn("[YST]", ...a);

function getSyncData() {
  return chrome.storage.sync.get(["groups", "watchedVideoIds"]);
}

function getLocalData() {
  return chrome.storage.local.get(["subscriptionsCache", "videosCache"]);
}

async function markWatched(videoId) {
  const { watchedVideoIds } = await getSyncData();
  const ids = new Set(watchedVideoIds || []);
  ids.add(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ watchedVideoIds: trimmed });
}

// --- Sidebar -------------------------------------------------------------

// ytd-guide-renderer holds exactly two children: #sections (the guide entries,
// managed by Polymer's dom-repeat) and #footer (the "About / Press / ..."
// links). We anchor our node BETWEEN them — a direct child of
// ytd-guide-renderer, sibling of #sections, not inside it. Polymer only churns
// #sections' own children, so once placed here the node stays put across
// navigation (verified) instead of being dragged around / evicted. It renders
// at the bottom of the guide list, just above the footer links.
function findGuideSections() {
  return document.querySelector("ytd-guide-renderer > #sections");
}

// Set whenever the rows need rebuilding (groups changed, a group was
// selected). The observer-driven renderSidebar() calls are frequent and cheap:
// they only re-place the node and return unless this is set.
let sidebarDirty = true;

async function renderSidebar() {
  const sections = findGuideSections();
  if (!sections) return; // guide not built yet, or torn down entirely

  let section = document.getElementById("yst-groups-section");
  if (!section) {
    section = document.createElement("div");
    section.id = "yst-groups-section";
    section.className = "yst-sidebar-section";
    sidebarDirty = true; // fresh node → needs its rows
  }

  // (Re)attach right after #sections. Safe to run every observer tick: once
  // section is #sections' next sibling this is a no-op, and Polymer never
  // moves it (it lives outside #sections), so there is no placement fight.
  if (sections.nextElementSibling !== section) {
    sections.insertAdjacentElement("afterend", section);
  }

  if (!sidebarDirty) return;
  sidebarDirty = false;

  const { groups } = await getSyncData();
  const entries = Object.entries(groups || {});

  // replaceChildren, not innerHTML: YouTube serves a Trusted-Types CSP and
  // `el.innerHTML = ""` throws under it. (Isolated worlds are exempt today, but
  // that exemption is on Chrome's chopping block — no reason to depend on it.)
  section.replaceChildren();
  const title = document.createElement("div");
  title.className = "yst-sidebar-title";
  title.textContent = "My groups";
  section.appendChild(title);

  section.appendChild(buildGroupRow(ALL_KEY, "All subscriptions", null));
  for (const [groupId, group] of entries) {
    section.appendChild(buildGroupRow(groupId, group.name, group.channelIds.length));
  }

  const manageLink = document.createElement("div");
  manageLink.className = "yst-manage-link";
  manageLink.textContent = "Manage groups...";
  manageLink.addEventListener("click", () => {
    chrome.runtime.sendMessage({ type: "OPEN_DASHBOARD" });
  });
  section.appendChild(manageLink);
}

// Force the next renderSidebar() to rebuild the rows, then run it now.
function refreshSidebar() {
  sidebarDirty = true;
  renderSidebar().catch((e) => warn("renderSidebar failed", e));
}

function buildGroupRow(key, label, count) {
  const row = document.createElement("div");
  row.className = "yst-group-row" + (key === activeGroupKey ? " active" : "");
  row.dataset.groupKey = key;

  const name = document.createElement("span");
  name.textContent = label;
  row.appendChild(name);

  if (count !== null) {
    const countEl = document.createElement("span");
    countEl.className = "count";
    countEl.textContent = count;
    row.appendChild(countEl);
  }

  row.addEventListener("click", () => toggleOverlay(key));
  return row;
}

// [[value, label], ...] -> <select> with those <option>s.
function buildSelect(options) {
  const select = document.createElement("select");
  for (const [value, label] of options) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = label;
    select.appendChild(option);
  }
  return select;
}

// The guide hydrates late and is sometimes rebuilt on navigation, dropping our
// node. A permanent throttled observer re-runs renderSidebar() on guide
// mutations; renderSidebar() is idempotent — re-attaches the node only when
// it's missing, no-ops while the guide is absent, rebuilds rows only when
// sidebarDirty. Because the node lives outside #sections, re-attaching never
// fights Polymer, so this can run freely.
let sidebarObserver = null;

// Throttle, not debounce: YouTube mutates the body subtree continuously, so a
// debounce's timer would keep resetting and never fire. This runs on the
// leading edge and then at most once per `ms`, with a guaranteed trailing run.
function throttle(fn, ms) {
  let last = 0;
  let timer = null;
  return () => {
    const wait = ms - (Date.now() - last);
    if (wait <= 0) {
      last = Date.now();
      fn();
    } else if (!timer) {
      timer = setTimeout(() => {
        timer = null;
        last = Date.now();
        fn();
      }, wait);
    }
  };
}

function watchSidebar() {
  renderSidebar().catch((e) => warn("renderSidebar failed", e));
  if (sidebarObserver) return;
  sidebarObserver = new MutationObserver(
    throttle(() => renderSidebar().catch((e) => warn("renderSidebar failed", e)), 500)
  );
  sidebarObserver.observe(document.body, { childList: true, subtree: true });
}

// --- Overlay -------------------------------------------------------------

function getOverlay() {
  return document.getElementById("yst-overlay");
}

function closeOverlay() {
  getOverlay()?.remove();
  activeGroupKey = null;
  refreshSidebar(); // active-row highlight changed
}

async function toggleOverlay(groupKey) {
  if (activeGroupKey === groupKey && getOverlay()) {
    closeOverlay();
    return;
  }
  activeGroupKey = groupKey;
  refreshSidebar(); // active-row highlight changed
  await renderOverlay();
}

async function getVisibleVideos(groupKey) {
  const [{ groups, watchedVideoIds }, { subscriptionsCache, videosCache }] = await Promise.all([
    getSyncData(),
    getLocalData(),
  ]);

  const channelIds =
    groupKey === ALL_KEY
      ? Object.keys(subscriptionsCache || {})
      : (groups || {})[groupKey]?.channelIds ?? [];

  const videos = [];
  for (const channelId of channelIds) {
    for (const video of (videosCache || {})[channelId] ?? []) videos.push(video);
  }
  return {
    videos,
    watchedIds: new Set(watchedVideoIds || []),
    channelTitleOf: (id) => (subscriptionsCache || {})[id]?.title ?? "",
  };
}

async function renderOverlay() {
  let overlay = getOverlay();
  if (!overlay) {
    overlay = document.createElement("div");
    overlay.id = "yst-overlay";
    overlay.className = "yst-overlay";
    document.body.appendChild(overlay);
  }

  const { groups } = await getSyncData();
  const groupLabel = activeGroupKey === ALL_KEY ? "All subscriptions" : groups?.[activeGroupKey]?.name ?? "";

  overlay.replaceChildren(); // not innerHTML — Trusted-Types CSP, see renderSidebar
  const header = document.createElement("div");
  header.className = "yst-overlay-header";

  const heading = document.createElement("h2");
  heading.textContent = groupLabel;
  header.appendChild(heading);

  const typeSelect = buildSelect([
    ["all", "All types"],
    ["video", "Video"],
    ["short", "Short"],
    ["live", "Live"],
  ]);
  const durationSelect = buildSelect([
    ["any", "Any length"],
    ["under4", "< 4 min"],
    ["4to20", "4–20 min"],
    ["over20", "> 20 min"],
  ]);
  const uploadedSelect = buildSelect([
    ["any", "Any time"],
    ["today", "Today"],
    ["week", "This week"],
    ["month", "This month"],
  ]);
  const sortSelect = buildSelect([
    ["date", "Newest"],
    ["duration", "Duration"],
    ["views", "Most viewed"],
  ]);
  header.append(typeSelect, durationSelect, uploadedSelect, sortSelect);

  const hideWatchedLabel = document.createElement("label");
  const hideWatchedCheckbox = document.createElement("input");
  hideWatchedCheckbox.type = "checkbox";
  hideWatchedLabel.append(hideWatchedCheckbox, document.createTextNode(" Hide watched"));
  header.appendChild(hideWatchedLabel);

  const searchInput = document.createElement("input");
  searchInput.type = "search";
  searchInput.className = "yst-search";
  searchInput.placeholder = "Search title or channel";
  header.appendChild(searchInput);

  const closeBtn = document.createElement("button");
  closeBtn.className = "yst-close-btn";
  closeBtn.textContent = "Close";
  closeBtn.addEventListener("click", closeOverlay);
  header.appendChild(closeBtn);

  const feed = document.createElement("div");
  feed.className = "yst-feed";

  overlay.append(header, feed);

  const rerenderFeed = async () => {
    const { videos, watchedIds, channelTitleOf } = await getVisibleVideos(activeGroupKey);
    const filtered = applyFilters(videos, {
      type: typeSelect.value,
      sortBy: sortSelect.value,
      hideWatched: hideWatchedCheckbox.checked,
      watchedIds,
      duration: durationSelect.value,
      uploadedWithin: uploadedSelect.value,
      query: searchInput.value,
      channelTitleOf,
    });

    feed.replaceChildren();
    for (const video of filtered) {
      feed.appendChild(buildVideoCard(video, watchedIds.has(video.videoId), rerenderFeed));
    }
  };

  for (const control of [typeSelect, durationSelect, uploadedSelect, sortSelect, hideWatchedCheckbox]) {
    control.addEventListener("change", rerenderFeed);
  }
  searchInput.addEventListener("input", rerenderFeed);
  await rerenderFeed();
}

function buildVideoCard(video, watched, onWatchedChange) {
  const card = document.createElement("div");
  card.className = "yst-video-card" + (watched ? " watched" : "");

  const link = document.createElement("a");
  link.href = `/watch?v=${video.videoId}`;
  const img = document.createElement("img");
  img.src = video.thumbnail ?? "";
  link.appendChild(img);
  link.addEventListener("click", () => closeOverlay());

  const info = document.createElement("div");
  info.className = "info";
  const title = document.createElement("div");
  title.textContent = video.title;
  const meta = document.createElement("div");
  meta.className = "meta";
  meta.textContent = `${video.type} · ${video.viewCount.toLocaleString()} views`;

  const watchBtn = document.createElement("button");
  watchBtn.textContent = watched ? "Watched" : "Mark watched";
  watchBtn.addEventListener("click", async () => {
    await markWatched(video.videoId);
    onWatchedChange();
  });

  info.append(title, meta, watchBtn);
  card.append(link, info);
  return card;
}

// --- Location badge (watch pages) ---------------------------------------
//
// Merged in from the old content-location.js: a content script matched only to
// /watch* never injects on YouTube's SPA navigations (same document, History
// API), so it only ran on a hard page load. This file already runs on every
// youtube.com page and handles yt-navigate-finish, so the badge lives here now.

const BADGE_CLASS = "yst-location-badge";
// Bumped on every render; a slower in-flight render checks it and bails instead
// of appending a second badge (the cause of the duplicate badges).
let badgeToken = 0;

function parseChannelRef(href) {
  // Resolve against a base so a relative "/@name" and an absolute
  // "https://www.youtube.com/@name" collapse to the same pathname; the base
  // also means this never throws for the inputs we get.
  const path = new URL(href, "https://www.youtube.com").pathname;

  const handle = path.match(/^\/@([^/]+)\/?$/);
  if (handle) return { handle: decodeURIComponent(handle[1]) };

  const id = path.match(/^\/channel\/(UC[\w-]+)\/?$/);
  if (id) return { channelId: id[1] };

  // "/user/<name>", "/c/<name>" and anything else: not resolvable here.
  return null;
}

function getChannelRef() {
  // getAttribute keeps the raw "/@handle" form; .href would absolutise it.
  const href = document
    .querySelector("ytd-video-owner-renderer a[href]")
    ?.getAttribute("href");
  return href ? parseChannelRef(href) : null;
}

function findOwnerNameContainer() {
  return document.querySelector("ytd-video-owner-renderer #channel-name");
}

async function requestCountry(ref) {
  const response = await chrome.runtime.sendMessage({ type: "GET_CHANNEL_COUNTRY", ...ref });
  return response?.country ?? null;
}

function waitFor(getValue, { tries = 20, intervalMs = 250 } = {}) {
  return new Promise((resolve) => {
    let n = 0;
    const timer = setInterval(() => {
      const value = getValue();
      if (value || ++n >= tries) {
        clearInterval(timer);
        resolve(value || null);
      }
    }, intervalMs);
  });
}

async function renderBadge() {
  // Bump first, unconditionally: navigating away from a watch page must
  // invalidate an in-flight render so it doesn't append a stale badge.
  const token = ++badgeToken;
  if (location.pathname !== "/watch") return;

  const container = await waitFor(findOwnerNameContainer);
  const ref = getChannelRef();
  if (token !== badgeToken || !container || !ref) return;

  const country = await requestCountry(ref);
  if (token !== badgeToken) return;

  document.querySelectorAll(`.${BADGE_CLASS}`).forEach((el) => el.remove());
  const badge = document.createElement("span");
  badge.className = BADGE_CLASS;
  badge.textContent = country ?? "Location unknown";
  container.appendChild(badge);
}

// --- Lifecycle -------------------------------------------------------------

function onNavigate() {
  watchSidebar();
  renderBadge().catch((e) => warn("renderBadge failed", e));
}

onNavigate();
document.addEventListener("yt-navigate-finish", onNavigate);
document.addEventListener("yt-navigate-start", () => getOverlay()?.remove());

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.groups) {
    refreshSidebar();
  }
});
