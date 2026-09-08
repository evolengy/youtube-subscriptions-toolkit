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
// Pseudo-groups: stored video lists rather than channel sets (see feedFilter's
// hydrateVideoList). Their sidebar rows have no icon picker.
const LIKED_KEY = "__liked__";
const NI_KEY = "__ni__";
const PSEUDO_LABELS = { [LIKED_KEY]: "👍 Liked", [NI_KEY]: "⊘ Not interested" };
const isPseudoKey = (key) => key === LIKED_KEY || key === NI_KEY;
let activeGroupKey = null;

const { applyFilters, hydrateVideoList } = window.YSTFeed;
const { countNewPerGroup } = window.YSTGroupCounts;
const { buildGuideCss } = window.YSTDeclutter;
const { openEmojiPicker } = window.YSTEmoji;
const { make: makeIcon } = window.YSTIcons;

const DEFAULT_GROUP_ICON = "📁";

// Content-script failures are otherwise silent — surface them with a tag.
const warn = (...a) => console.warn("[YST]", ...a);

function getSyncData() {
  return chrome.storage.sync.get([
    "groups",
    "watchedVideoIds",
    "notInterestedVideoIds",
    "groupLastVisited",
  ]);
}

// --- Guide declutter (hide chosen sections of YouTube's own left menu) ------

// One <style> in <head>; CSS only, so Polymer never fights it and it survives
// every SPA re-render. Rebuilt from the `guideHidden` setting on change.
async function applyGuideDeclutter() {
  const { guideHidden } = await chrome.storage.sync.get("guideHidden");
  let style = document.getElementById("yst-guide-style");
  if (!style) {
    style = document.createElement("style");
    style.id = "yst-guide-style";
    document.head.appendChild(style);
  }
  style.textContent = buildGuideCss(guideHidden || {});
}

// "N new since last opened" bookkeeping for the group badges — written straight
// to sync like markWatched(); storage.onChanged re-renders the sidebar.
async function touchGroupVisited(groupKey) {
  const { groupLastVisited } = await chrome.storage.sync.get("groupLastVisited");
  const map = groupLastVisited || {};
  map[groupKey] = Date.now();
  await chrome.storage.sync.set({ groupLastVisited: map });
}

// storage.js is an ES module the dashboard imports; the content script writes
// chrome.storage.sync directly, same as markWatched(). The storage.onChanged
// listener below re-renders the sidebar afterwards.
async function setGroupIcon(groupId, icon) {
  const { groups } = await chrome.storage.sync.get("groups");
  const map = groups || {};
  if (!map[groupId]) return;
  if (icon) map[groupId].icon = icon;
  else delete map[groupId].icon;
  await chrome.storage.sync.set({ groups: map });
}

function getLocalData() {
  return chrome.storage.local.get([
    "subscriptionsCache",
    "videosCache",
    "likedVideos",
    "notInterestedVideos",
    "prevSyncedAt",
    "lastSyncedAt",
  ]);
}

async function markWatched(videoId) {
  const { watchedVideoIds } = await getSyncData();
  const ids = new Set(watchedVideoIds || []);
  ids.add(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ watchedVideoIds: trimmed });
}

// `video` is a full card object when adding (feeds the metadata map that backs
// the standalone "Not interested" list), or a bare id when removing.
async function toggleNotInterested(video, add) {
  const videoId = typeof video === "string" ? video : video.videoId;
  const { notInterestedVideoIds } = await getSyncData();
  const ids = new Set(notInterestedVideoIds || []);
  if (add) ids.add(videoId);
  else ids.delete(videoId);
  const trimmed = Array.from(ids).slice(-MAX_WATCHED_IDS);
  await chrome.storage.sync.set({ notInterestedVideoIds: trimmed });

  // Mirror into the local metadata map, kept in lockstep with the trimmed ids.
  const { notInterestedVideos } = await chrome.storage.local.get("notInterestedVideos");
  const map = notInterestedVideos || {};
  if (add && typeof video === "object") {
    map[videoId] = {
      videoId,
      title: video.title ?? null,
      thumbnail: video.thumbnail ?? null,
      channelId: video.channelId ?? null,
      publishedAt: video.publishedAt ?? null,
    };
  } else {
    delete map[videoId];
  }
  const keep = new Set(trimmed);
  for (const id of Object.keys(map)) if (!keep.has(id)) delete map[id];
  await chrome.storage.local.set({ notInterestedVideos: map });
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

  const [
    { groups, watchedVideoIds, notInterestedVideoIds, groupLastVisited },
    { subscriptionsCache, videosCache, likedVideos, prevSyncedAt, lastSyncedAt },
  ] = await Promise.all([getSyncData(), getLocalData()]);
  const entries = Object.entries(groups || {});

  const newCounts = countNewPerGroup(groups || {}, videosCache || {}, {
    lastVisited: groupLastVisited || {},
    fallbackSince: prevSyncedAt ?? lastSyncedAt ?? null,
    watchedIds: new Set(watchedVideoIds || []),
    notInterestedIds: new Set(notInterestedVideoIds || []),
    allChannelIds: Object.keys(subscriptionsCache || {}),
  });

  // replaceChildren, not innerHTML: YouTube serves a Trusted-Types CSP and
  // `el.innerHTML = ""` throws under it. (Isolated worlds are exempt today, but
  // that exemption is on Chrome's chopping block — no reason to depend on it.)
  section.replaceChildren();
  const title = document.createElement("div");
  title.className = "yst-sidebar-title";
  title.textContent = "My groups";
  section.appendChild(title);

  section.appendChild(buildGroupRow(ALL_KEY, "All subscriptions", null, "", newCounts[ALL_KEY]));
  for (const [groupId, group] of entries) {
    section.appendChild(
      buildGroupRow(groupId, group.name, group.channelIds.length, group.icon, newCounts[groupId])
    );
  }
  section.appendChild(
    buildGroupRow(LIKED_KEY, PSEUDO_LABELS[LIKED_KEY], Object.keys(likedVideos || {}).length, "", 0)
  );
  section.appendChild(
    buildGroupRow(NI_KEY, PSEUDO_LABELS[NI_KEY], (notInterestedVideoIds || []).length, "", 0)
  );

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

function buildGroupRow(key, label, count, icon, newCount = 0) {
  const row = document.createElement("div");
  row.className = "yst-group-row" + (key === activeGroupKey ? " active" : "");
  row.dataset.groupKey = key;

  const main = document.createElement("span");
  main.className = "yst-group-main";

  if (key !== ALL_KEY && !isPseudoKey(key)) {
    // Clickable icon — opens the picker instead of switching to the group.
    const iconEl = document.createElement("button");
    iconEl.type = "button";
    iconEl.className = "yst-group-icon";
    iconEl.textContent = icon || DEFAULT_GROUP_ICON;
    iconEl.title = "Change icon";
    iconEl.addEventListener("click", (event) => {
      event.stopPropagation();
      openEmojiPicker(iconEl, {
        current: icon,
        onPick: (char) => setGroupIcon(key, char),
        onClear: () => setGroupIcon(key, ""),
      });
    });
    main.appendChild(iconEl);
  }

  const name = document.createElement("span");
  name.textContent = label;
  main.appendChild(name);
  row.appendChild(main);

  const trailing = document.createElement("span");
  trailing.className = "yst-group-trailing";
  if (newCount > 0) {
    const badge = document.createElement("span");
    badge.className = "yst-new-badge";
    badge.textContent = newCount > 99 ? "99+" : newCount;
    trailing.appendChild(badge);
  }
  if (count !== null) {
    const countEl = document.createElement("span");
    countEl.className = "count";
    countEl.textContent = count;
    trailing.appendChild(countEl);
  }
  if (trailing.childNodes.length) row.appendChild(trailing);

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
  if (!isPseudoKey(groupKey)) await touchGroupVisited(groupKey); // clears its "N new" badge
  refreshSidebar(); // active-row highlight + badge changed
  await renderOverlay();
}

async function getVisibleVideos(groupKey) {
  const [
    { groups, watchedVideoIds, notInterestedVideoIds },
    { subscriptionsCache, videosCache, likedVideos, notInterestedVideos },
  ] = await Promise.all([getSyncData(), getLocalData()]);

  const common = {
    watchedIds: new Set(watchedVideoIds || []),
    notInterestedIds: new Set(notInterestedVideoIds || []),
    likedIds: new Set(Object.keys(likedVideos || {})),
    channelTitleOf: (id) => (subscriptionsCache || {})[id]?.title ?? "",
  };

  if (isPseudoKey(groupKey)) {
    const flat = [];
    for (const list of Object.values(videosCache || {})) flat.push(...list);
    const [ids, metaMap] =
      groupKey === LIKED_KEY
        ? [Object.keys(likedVideos || {}), likedVideos || {}]
        : [notInterestedVideoIds || [], notInterestedVideos || {}];
    return { ...common, videos: hydrateVideoList(ids, metaMap, flat) };
  }

  const channelIds =
    groupKey === ALL_KEY
      ? Object.keys(subscriptionsCache || {})
      : (groups || {})[groupKey]?.channelIds ?? [];

  const videos = [];
  for (const channelId of channelIds) {
    for (const video of (videosCache || {})[channelId] ?? []) videos.push(video);
  }
  return { ...common, videos };
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
  const isPseudo = isPseudoKey(activeGroupKey);
  const group = activeGroupKey === ALL_KEY || isPseudo ? null : groups?.[activeGroupKey];
  const groupLabel = activeGroupKey === ALL_KEY ? "All subscriptions" : group?.name ?? "";

  overlay.replaceChildren(); // not innerHTML — Trusted-Types CSP, see renderSidebar
  const header = document.createElement("div");
  header.className = "yst-overlay-header";

  const heading = document.createElement("h2");
  heading.textContent = isPseudo
    ? PSEUDO_LABELS[activeGroupKey]
    : group
    ? `${group.icon || DEFAULT_GROUP_ICON} ${groupLabel}`
    : groupLabel;
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
  hideWatchedCheckbox.checked = true;
  hideWatchedLabel.append(hideWatchedCheckbox, document.createTextNode(" Hide watched"));
  header.appendChild(hideWatchedLabel);

  const likedWatchedLabel = document.createElement("label");
  const likedWatchedCheckbox = document.createElement("input");
  likedWatchedCheckbox.type = "checkbox";
  likedWatchedCheckbox.checked = true;
  likedWatchedLabel.append(likedWatchedCheckbox, document.createTextNode(" Liked = watched"));
  header.appendChild(likedWatchedLabel);

  const showNiLabel = document.createElement("label");
  const showNiCheckbox = document.createElement("input");
  showNiCheckbox.type = "checkbox";
  showNiLabel.append(showNiCheckbox, document.createTextNode(" Show not interested"));
  header.appendChild(showNiLabel);

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
    const { videos, watchedIds, notInterestedIds, likedIds, channelTitleOf } =
      await getVisibleVideos(activeGroupKey);
    const likedAsWatched = likedWatchedCheckbox.checked;
    const filtered = applyFilters(videos, {
      type: typeSelect.value,
      sortBy: sortSelect.value,
      hideWatched: hideWatchedCheckbox.checked,
      watchedIds,
      duration: durationSelect.value,
      uploadedWithin: uploadedSelect.value,
      query: searchInput.value,
      channelTitleOf,
      notInterestedIds,
      showNotInterested: activeGroupKey === NI_KEY || showNiCheckbox.checked,
      likedIds,
      likedAsWatched,
    });

    const pseudo = isPseudoKey(activeGroupKey);
    feed.replaceChildren();
    for (const video of filtered) {
      feed.appendChild(
        buildVideoCard(video, {
          watched: watchedIds.has(video.videoId) || (video.liked && likedAsWatched),
          notInterested: notInterestedIds.has(video.videoId),
          liked: video.liked,
          pseudo,
          onChange: rerenderFeed,
        })
      );
    }
  };

  for (const control of [
    typeSelect,
    durationSelect,
    uploadedSelect,
    sortSelect,
    hideWatchedCheckbox,
    likedWatchedCheckbox,
    showNiCheckbox,
  ]) {
    control.addEventListener("change", rerenderFeed);
  }
  searchInput.addEventListener("input", rerenderFeed);
  await rerenderFeed();
}

function iconButton(name, label) {
  const btn = document.createElement("button");
  btn.className = "yst-card-action";
  btn.title = label;
  btn.setAttribute("aria-label", label);
  btn.appendChild(makeIcon(name));
  return btn;
}

function buildVideoCard(video, { watched, notInterested, liked, pseudo, onChange }) {
  const card = document.createElement("div");
  // See dashboard.js: no whole-grid dimming inside a pseudo-group.
  card.className =
    "yst-video-card" +
    (watched && !pseudo ? " watched" : "") +
    (notInterested && !pseudo ? " not-interested" : "");

  const link = document.createElement("a");
  link.className = "thumb";
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
  meta.append(`${video.type} · ${(video.viewCount ?? 0).toLocaleString()} views`);
  if (liked) {
    const wrap = document.createElement("span");
    wrap.className = "yst-liked-mark";
    wrap.title = "Liked on YouTube";
    wrap.appendChild(makeIcon("thumb", { size: 14 }));
    meta.append(" ", wrap);
  }

  const actions = document.createElement("div");
  actions.className = "yst-card-actions";

  const watchBtn = iconButton("check", watched ? "Watched" : "Mark watched");
  watchBtn.classList.toggle("on", watched);
  watchBtn.addEventListener("click", async () => {
    await markWatched(video.videoId);
    onChange();
  });

  const niBtn = iconButton(
    notInterested ? "undo" : "ban",
    notInterested ? "Restore" : "Not interested"
  );
  niBtn.addEventListener("click", async () => {
    await toggleNotInterested(notInterested ? video.videoId : video, !notInterested);
    onChange();
  });

  actions.append(watchBtn, niBtn);
  info.append(title, meta, actions);
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
applyGuideDeclutter().catch((e) => warn("applyGuideDeclutter failed", e));
document.addEventListener("yt-navigate-finish", onNavigate);
document.addEventListener("yt-navigate-start", () => getOverlay()?.remove());

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.guideHidden) {
    applyGuideDeclutter().catch((e) => warn("applyGuideDeclutter failed", e));
  }
  if (
    area === "sync" &&
    (changes.groups ||
      changes.notInterestedVideoIds ||
      changes.watchedVideoIds ||
      changes.groupLastVisited)
  ) {
    refreshSidebar(); // rows, counts or "N new" badges changed
  }
  if (
    area === "local" &&
    (changes.likedVideos || changes.videosCache || changes.prevSyncedAt)
  ) {
    refreshSidebar(); // a sync refreshed the liked list / videos / the "new" baseline
  }
});
