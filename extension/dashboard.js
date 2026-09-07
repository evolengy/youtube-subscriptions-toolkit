import * as store from "./storage.js";

// Plain <script>s in dashboard.html, ahead of this module.
const { applyFilters } = window.YSTFeed;
const { make: makeIcon } = window.YSTIcons;

const DEFAULT_GROUP_ICON = "📁";

let subscriptions = {};
let videosByChannel = {};
let groups = {};
let watchedIds = new Set();
let notInterestedIds = new Set();
let likedIds = new Set();
let activeGroupId = null;

const el = (id) => document.getElementById(id);

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response?.error) throw new Error(response.error);
  return response;
}

async function loadState() {
  let likedVideos;
  [subscriptions, videosByChannel, groups, watchedIds, notInterestedIds, likedVideos] =
    await Promise.all([
      store.getSubscriptionsCache(),
      store.getVideosCache(),
      store.getGroups(),
      store.getWatchedVideoIds(),
      store.getNotInterestedVideoIds(),
      store.getLikedVideos(),
    ]);
  likedIds = new Set(Object.keys(likedVideos));
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

  const selectRow = (id, label, isActive) => {
    const item = document.createElement("li");
    item.className = isActive ? "active" : "";
    item.textContent = label;
    item.addEventListener("click", () => {
      activeGroupId = id;
      renderGroups();
      renderFeed();
    });
    return item;
  };

  list.appendChild(selectRow(null, "All subscriptions", activeGroupId === null));
  for (const [groupId, group] of Object.entries(groups)) {
    const icon = group.icon || DEFAULT_GROUP_ICON;
    list.appendChild(
      selectRow(groupId, `${icon} ${group.name} (${group.channelIds.length})`, groupId === activeGroupId)
    );
  }
}

// A group edited in groups.html / the YouTube sidebar should refresh this list.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.groups) {
    store.getGroups().then((g) => {
      groups = g;
      if (activeGroupId && !groups[activeGroupId]) activeGroupId = null;
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

function collectVideos() {
  const channelIds = getVisibleChannelIds();
  const videos = [];
  for (const channelId of channelIds) {
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
    showNotInterested: el("showNotInterested").checked,
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

  const card = document.createElement("div");
  card.className =
    "video-card" + (watched ? " watched" : "") + (notInterested ? " not-interested" : "");

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
  meta.append(`${video.type} · ${video.viewCount.toLocaleString()} views`);
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
    renderFeed();
  });

  const niBtn = iconButton(
    notInterested ? "undo" : "ban",
    notInterested ? "Restore" : "Not interested"
  );
  niBtn.addEventListener("click", async () => {
    if (notInterested) await store.removeNotInterested(video.videoId);
    else await store.addNotInterested(video.videoId);
    notInterestedIds = await store.getNotInterestedVideoIds();
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
