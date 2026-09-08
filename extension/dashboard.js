import * as store from "./storage.js";

// Plain <script>s in dashboard.html, ahead of this module.
const { applyFilters, hydrateVideoList } = window.YSTFeed;
const { countNewPerGroup, ALL_KEY } = window.YSTGroupCounts;
const { make: makeIcon } = window.YSTIcons;

const DEFAULT_GROUP_ICON = "📁";

// Pseudo-groups: not channel sets but stored video lists (the full liked and
// "not interested" lists, which reach past the recent-uploads cache window).
const LIKED_KEY = "__liked__";
const NI_KEY = "__ni__";

let subscriptions = {};
let videosByChannel = {};
let groups = {};
let watchedIds = new Set();
let notInterestedIds = new Set();
let notInterestedMeta = {};
let likedVideos = {};
let likedIds = new Set();
let groupLastVisited = {};
let syncFallback = null; // "since" for a group with no lastVisited entry
let newCounts = {};
let activeGroupId = null;

const el = (id) => document.getElementById(id);

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response?.error) throw new Error(response.error);
  return response;
}

async function loadState() {
  [
    subscriptions,
    videosByChannel,
    groups,
    watchedIds,
    notInterestedIds,
    notInterestedMeta,
    likedVideos,
    groupLastVisited,
  ] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getVideosCache(),
    store.getGroups(),
    store.getWatchedVideoIds(),
    store.getNotInterestedVideoIds(),
    store.getNotInterestedVideos(),
    store.getLikedVideos(),
    store.getGroupLastVisited(),
  ]);
  likedIds = new Set(Object.keys(likedVideos));

  const [prevSyncedAt, lastSyncedAt] = await Promise.all([
    store.getPrevSyncedAt(),
    store.getLastSyncedAt(),
  ]);
  syncFallback = prevSyncedAt ?? lastSyncedAt;
  recomputeNewCounts();
}

function recomputeNewCounts() {
  newCounts = countNewPerGroup(groups, videosByChannel, {
    lastVisited: groupLastVisited,
    fallbackSince: syncFallback,
    watchedIds,
    notInterestedIds,
    allChannelIds: Object.keys(subscriptions),
  });
}

// --- Auth -------------------------------------------------------------

async function refreshAuthUI() {
  const { signedIn } = await send({ type: "GET_AUTH_STATUS" });
  el("signInBtn").hidden = signedIn;
  el("signOutBtn").hidden = !signedIn;
  el("refreshBtn").hidden = !signedIn;
  el("manageChannelsBtn").hidden = !signedIn;
  el("editGroupsBtn").hidden = !signedIn;
  el("app").hidden = !signedIn;
  if (signedIn) await loadAndRender();
}

const openPage = (file) => () => chrome.tabs.create({ url: chrome.runtime.getURL(file) });
el("manageChannelsBtn").addEventListener("click", openPage("channels.html"));
el("editGroupsBtn").addEventListener("click", openPage("groups.html"));
el("settingsBtn").addEventListener("click", openPage("settings.html")); // no auth needed

el("signInBtn").addEventListener("click", async () => {
  el("syncStatus").textContent = "Signing in...";
  try {
    await send({ type: "SIGN_IN" });
    el("syncStatus").textContent = "";
  } catch (err) {
    console.error(err);
    el("syncStatus").textContent = `Error: ${err.message}`;
  }
  await refreshAuthUI();
});

el("signOutBtn").addEventListener("click", async () => {
  await send({ type: "SIGN_OUT" });
  await refreshAuthUI();
});

el("refreshBtn").addEventListener("click", async () => {
  el("syncStatus").textContent = "Refreshing...";
  try {
    await send({ type: "REFRESH_ALL" });
    el("syncStatus").textContent = "";
  } catch (err) {
    console.error(err);
    el("syncStatus").textContent = `Error: ${err.message}`;
    return;
  }
  await loadAndRender();
});

async function loadAndRender() {
  await loadState();
  renderSyncStatus();
  renderGroups();
  renderFeed();
}

async function renderSyncStatus() {
  const ts = await store.getLastSyncedAt();
  el("syncStatus").textContent = ts ? `Synced ${new Date(ts).toLocaleTimeString()}` : "";
}

// --- Groups (read-only list; a feed filter — CRUD lives in groups.html) --

function renderGroups() {
  const list = el("groupList");
  list.replaceChildren();

  // countKey: which newCounts entry this row shows a badge for (null for the
  // pseudo-groups, which have no "new" notion).
  const selectRow = (id, label, isActive, countKey) => {
    const item = document.createElement("li");
    item.className = isActive ? "active" : "";
    item.append(label);

    const n = countKey ? newCounts[countKey] ?? 0 : 0;
    if (n > 0) {
      const badge = document.createElement("span");
      badge.className = "new-badge";
      badge.textContent = n > 99 ? "99+" : n;
      item.append(" ", badge);
    }

    item.addEventListener("click", async () => {
      activeGroupId = id;
      if (countKey) {
        await store.touchGroupVisited(countKey);
        groupLastVisited = await store.getGroupLastVisited();
        recomputeNewCounts();
      }
      renderGroups();
      renderFeed();
    });
    return item;
  };

  list.appendChild(selectRow(null, "All subscriptions", activeGroupId === null, ALL_KEY));
  for (const [groupId, group] of Object.entries(groups)) {
    const icon = group.icon || DEFAULT_GROUP_ICON;
    list.appendChild(
      selectRow(
        groupId,
        `${icon} ${group.name} (${group.channelIds.length})`,
        groupId === activeGroupId,
        groupId
      )
    );
  }

  list.appendChild(
    selectRow(LIKED_KEY, `👍 Liked (${likedIds.size})`, activeGroupId === LIKED_KEY, null)
  );
  list.appendChild(
    selectRow(NI_KEY, `⊘ Not interested (${notInterestedIds.size})`, activeGroupId === NI_KEY, null)
  );
}

// A group edited in groups.html / the YouTube sidebar should refresh this list.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.groups) {
    store.getGroups().then((g) => {
      groups = g;
      const isPseudo = activeGroupId === LIKED_KEY || activeGroupId === NI_KEY;
      if (activeGroupId && !isPseudo && !groups[activeGroupId]) activeGroupId = null;
      recomputeNewCounts();
      renderGroups();
      renderFeed();
    });
  }
});

// --- Feed -------------------------------------------------------------

function getVisibleChannelIds() {
  if (!activeGroupId) return Object.keys(subscriptions);
  return groups[activeGroupId]?.channelIds ?? [];
}

function flattenCache() {
  const all = [];
  for (const list of Object.values(videosByChannel)) all.push(...list);
  return all;
}

function collectVideos() {
  if (activeGroupId === LIKED_KEY) {
    return hydrateVideoList(Object.keys(likedVideos), likedVideos, flattenCache());
  }
  if (activeGroupId === NI_KEY) {
    return hydrateVideoList([...notInterestedIds], notInterestedMeta, flattenCache());
  }
  const videos = [];
  for (const channelId of getVisibleChannelIds()) {
    for (const video of videosByChannel[channelId] ?? []) {
      videos.push(video);
    }
  }
  return videos;
}

function renderFeed() {
  const feed = el("feed");
  feed.innerHTML = "";

  const videos = applyFilters(collectVideos(), {
    type: el("filterType").value,
    sortBy: el("sortBy").value,
    hideWatched: el("hideWatched").checked,
    watchedIds,
    duration: el("filterDuration").value,
    uploadedWithin: el("filterUploaded").value,
    query: el("searchQuery").value,
    channelTitleOf: (channelId) => subscriptions[channelId]?.title ?? "",
    notInterestedIds,
    showNotInterested: activeGroupId === NI_KEY || el("showNotInterested").checked,
    likedIds,
    likedAsWatched: el("likedAsWatched").checked,
  });

  for (const video of videos) {
    feed.appendChild(renderVideoCard(video));
  }
}

function iconButton(name, label) {
  const btn = document.createElement("button");
  btn.className = "card-action";
  btn.title = label;
  btn.setAttribute("aria-label", label);
  btn.appendChild(makeIcon(name));
  return btn;
}

function renderVideoCard(video) {
  const liked = video.liked;
  const watched = watchedIds.has(video.videoId) || (liked && el("likedAsWatched").checked);
  const notInterested = notInterestedIds.has(video.videoId);
  // Inside a pseudo-group every card is watched/liked/not-interested by
  // definition — dimming the whole grid conveys nothing, so skip it there.
  const pseudoView = activeGroupId === LIKED_KEY || activeGroupId === NI_KEY;

  const card = document.createElement("div");
  card.className =
    "video-card" +
    (watched && !pseudoView ? " watched" : "") +
    (notInterested && !pseudoView ? " not-interested" : "");

  const link = document.createElement("a");
  link.href = `https://www.youtube.com/watch?v=${video.videoId}`;
  link.target = "_blank";
  const img = document.createElement("img");
  img.src = video.thumbnail ?? "";
  link.appendChild(img);

  const info = document.createElement("div");
  info.className = "info";
  const title = document.createElement("div");
  title.textContent = video.title;
  const meta = document.createElement("div");
  meta.className = "meta";
  meta.append(`${video.type} · ${(video.viewCount ?? 0).toLocaleString()} views`);
  if (liked) {
    const thumb = makeIcon("thumb", { size: 14 });
    thumb.classList.add("liked-mark");
    const wrap = document.createElement("span");
    wrap.className = "liked-mark-wrap";
    wrap.title = "Liked on YouTube";
    wrap.appendChild(thumb);
    meta.append(" ", wrap);
  }

  const actions = document.createElement("div");
  actions.className = "card-actions";

  const watchBtn = iconButton("check", watched ? "Watched" : "Mark watched");
  watchBtn.classList.toggle("on", watched);
  watchBtn.addEventListener("click", async () => {
    await store.markVideoWatched(video.videoId);
    watchedIds = await store.getWatchedVideoIds();
    recomputeNewCounts();
    renderGroups();
    renderFeed();
  });

  const niBtn = iconButton(
    notInterested ? "undo" : "ban",
    notInterested ? "Restore" : "Not interested"
  );
  niBtn.addEventListener("click", async () => {
    if (notInterested) await store.removeNotInterested(video.videoId);
    else await store.addNotInterested(video);
    [notInterestedIds, notInterestedMeta] = await Promise.all([
      store.getNotInterestedVideoIds(),
      store.getNotInterestedVideos(),
    ]);
    recomputeNewCounts();
    renderGroups();
    renderFeed();
  });

  actions.append(watchBtn, niBtn);
  info.append(title, meta, actions);
  card.append(link, info);
  return card;
}

for (const id of [
  "filterType",
  "filterDuration",
  "filterUploaded",
  "sortBy",
  "hideWatched",
  "likedAsWatched",
  "showNotInterested",
]) {
  el(id).addEventListener("change", renderFeed);
}
el("searchQuery").addEventListener("input", renderFeed);

refreshAuthUI();
