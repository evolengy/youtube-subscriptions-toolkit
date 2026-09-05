// Injects a "My groups" section into YouTube's own left sidebar, and an
// overlay (docked below the masthead) that renders the grouped feed without
// touching YouTube's actual feed DOM. Group/channel management stays in
// dashboard.html — this is a read/switch surface, reusing masthead search
// and navigation underneath.
//
// Plain script (not an ES module) — content scripts can't use import/export
// against a separate module file without extra manifest wiring, so the
// small bits of logic shared with dashboard.js (duration parsing, type
// classification) are duplicated here rather than imported.

const MAX_WATCHED_IDS = 2000;
const ALL_KEY = "__all__";
let activeGroupKey = null;

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

function parseIsoDuration(iso) {
  const match = /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso ?? "");
  if (!match) return 0;
  const [, h, m, s] = match;
  return (Number(h) || 0) * 3600 + (Number(m) || 0) * 60 + (Number(s) || 0);
}

const SHORT_MAX_SECONDS = 60;

function classifyVideoType(video, durationSeconds) {
  if (video.liveBroadcastContent === "live" || video.liveBroadcastContent === "upcoming") {
    return "live";
  }
  return durationSeconds <= SHORT_MAX_SECONDS ? "short" : "video";
}

// --- Sidebar -------------------------------------------------------------

function findSidebarSections() {
  return document.querySelector("ytd-guide-renderer #sections");
}

async function renderSidebar() {
  const sections = findSidebarSections();
  if (!sections) return;

  let section = sections.querySelector("#yst-groups-section");
  if (!section) {
    section = document.createElement("div");
    section.id = "yst-groups-section";
    section.className = "yst-sidebar-section";
    sections.appendChild(section);
  }

  const { groups } = await getSyncData();
  const entries = Object.entries(groups || {});

  section.innerHTML = "";
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

// The guide sidebar is hydrated client-side and can take anywhere from
// under a second to several seconds to appear (cold loads are slow), and it
// gets torn down and rebuilt on some SPA navigations. A MutationObserver
// waits it out indefinitely instead of giving up after a fixed number of
// polling attempts.
let sidebarObserver = null;

function ensureSidebarInjected() {
  if (findSidebarSections()) {
    renderSidebar().catch(console.error);
    return;
  }
  if (sidebarObserver) return;
  sidebarObserver = new MutationObserver(() => {
    if (findSidebarSections()) {
      sidebarObserver.disconnect();
      sidebarObserver = null;
      renderSidebar().catch(console.error);
    }
  });
  sidebarObserver.observe(document.body, { childList: true, subtree: true });
}

// --- Overlay -------------------------------------------------------------

function getOverlay() {
  return document.getElementById("yst-overlay");
}

function closeOverlay() {
  getOverlay()?.remove();
  activeGroupKey = null;
  renderSidebar().catch(console.error);
}

async function toggleOverlay(groupKey) {
  if (activeGroupKey === groupKey && getOverlay()) {
    closeOverlay();
    return;
  }
  activeGroupKey = groupKey;
  await renderSidebar();
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
    for (const video of (videosCache || {})[channelId] ?? []) {
      const durationSeconds = parseIsoDuration(video.duration);
      videos.push({ ...video, durationSeconds, type: classifyVideoType(video, durationSeconds) });
    }
  }
  return { videos, watchedIds: new Set(watchedVideoIds || []) };
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

  overlay.innerHTML = "";
  const header = document.createElement("div");
  header.className = "yst-overlay-header";

  const heading = document.createElement("h2");
  heading.textContent = groupLabel;
  header.appendChild(heading);

  const typeSelect = document.createElement("select");
  typeSelect.innerHTML = `
    <option value="all">All types</option>
    <option value="video">Video</option>
    <option value="short">Short</option>
    <option value="live">Live</option>
  `;
  header.appendChild(typeSelect);

  const sortSelect = document.createElement("select");
  sortSelect.innerHTML = `
    <option value="date">Newest</option>
    <option value="duration">Duration</option>
    <option value="views">Most viewed</option>
  `;
  header.appendChild(sortSelect);

  const hideWatchedLabel = document.createElement("label");
  const hideWatchedCheckbox = document.createElement("input");
  hideWatchedCheckbox.type = "checkbox";
  hideWatchedLabel.append(hideWatchedCheckbox, document.createTextNode(" Hide watched"));
  header.appendChild(hideWatchedLabel);

  const closeBtn = document.createElement("button");
  closeBtn.className = "yst-close-btn";
  closeBtn.textContent = "Close";
  closeBtn.addEventListener("click", closeOverlay);
  header.appendChild(closeBtn);

  const feed = document.createElement("div");
  feed.className = "yst-feed";

  overlay.append(header, feed);

  const rerenderFeed = async () => {
    const { videos, watchedIds } = await getVisibleVideos(activeGroupKey);
    let filtered = videos;
    if (typeSelect.value !== "all") filtered = filtered.filter((v) => v.type === typeSelect.value);
    if (hideWatchedCheckbox.checked) filtered = filtered.filter((v) => !watchedIds.has(v.videoId));

    filtered.sort((a, b) => {
      if (sortSelect.value === "duration") return b.durationSeconds - a.durationSeconds;
      if (sortSelect.value === "views") return b.viewCount - a.viewCount;
      return new Date(b.publishedAt) - new Date(a.publishedAt);
    });

    feed.innerHTML = "";
    for (const video of filtered) {
      feed.appendChild(buildVideoCard(video, watchedIds.has(video.videoId), rerenderFeed));
    }
  };

  typeSelect.addEventListener("change", rerenderFeed);
  sortSelect.addEventListener("change", rerenderFeed);
  hideWatchedCheckbox.addEventListener("change", rerenderFeed);
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

// --- Lifecycle -------------------------------------------------------------

ensureSidebarInjected();
document.addEventListener("yt-navigate-finish", ensureSidebarInjected);
document.addEventListener("yt-navigate-start", () => getOverlay()?.remove());

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.groups) {
    renderSidebar().catch(console.error);
  }
});
