import * as store from "./storage.js";

let subscriptions = {};
let videosByChannel = {};
let groups = {};
let watchedIds = new Set();
let activeGroupId = null;

const el = (id) => document.getElementById(id);

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response?.error) throw new Error(response.error);
  return response;
}

async function loadState() {
  [subscriptions, videosByChannel, groups, watchedIds] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getVideosCache(),
    store.getGroups(),
    store.getWatchedVideoIds(),
  ]);
}

// --- Auth -------------------------------------------------------------

async function refreshAuthUI() {
  const { signedIn } = await send({ type: "GET_AUTH_STATUS" });
  el("signInBtn").hidden = signedIn;
  el("signOutBtn").hidden = !signedIn;
  el("refreshBtn").hidden = !signedIn;
  el("app").hidden = !signedIn;
  if (signedIn) await loadAndRender();
}

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
  renderChannelAssignList();
  renderFeed();
  renderDeadChannels();
}

async function renderSyncStatus() {
  const ts = await store.getLastSyncedAt();
  el("syncStatus").textContent = ts ? `Synced ${new Date(ts).toLocaleTimeString()}` : "";
}

// --- Groups -------------------------------------------------------------

function renderGroups() {
  const list = el("groupList");
  list.innerHTML = "";

  const allItem = document.createElement("li");
  allItem.textContent = "All subscriptions";
  allItem.className = activeGroupId === null ? "active" : "";
  allItem.addEventListener("click", () => {
    activeGroupId = null;
    renderGroups();
    renderChannelAssignList();
    renderFeed();
  });
  list.appendChild(allItem);

  for (const [groupId, group] of Object.entries(groups)) {
    const item = document.createElement("li");
    item.className = groupId === activeGroupId ? "active" : "";

    const label = document.createElement("span");
    label.textContent = `${group.name} (${group.channelIds.length})`;
    label.addEventListener("click", () => {
      activeGroupId = groupId;
      renderGroups();
      renderChannelAssignList();
      renderFeed();
    });

    const del = document.createElement("button");
    del.textContent = "x";
    del.addEventListener("click", async (evt) => {
      evt.stopPropagation();
      await store.deleteGroup(groupId);
      groups = await store.getGroups();
      if (activeGroupId === groupId) activeGroupId = null;
      renderGroups();
      renderChannelAssignList();
      renderFeed();
    });

    item.append(label, del);
    list.appendChild(item);
  }
}

el("newGroupForm").addEventListener("submit", async (evt) => {
  evt.preventDefault();
  const nameInput = el("newGroupName");
  const name = nameInput.value.trim();
  if (!name) return;
  const groupId = `g_${Date.now()}`;
  groups = await store.upsertGroup(groupId, name, []);
  nameInput.value = "";
  renderGroups();
});

function renderChannelAssignList() {
  const container = el("channelAssignList");
  container.innerHTML = "";
  if (!activeGroupId) {
    container.textContent = "Select a group to assign channels to it.";
    return;
  }
  const group = groups[activeGroupId];
  for (const [channelId, sub] of Object.entries(subscriptions)) {
    if (sub.dead) continue;
    const label = document.createElement("label");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = group.channelIds.includes(channelId);
    checkbox.addEventListener("change", async () => {
      const next = checkbox.checked
        ? [...group.channelIds, channelId]
        : group.channelIds.filter((id) => id !== channelId);
      groups = await store.upsertGroup(activeGroupId, group.name, next);
      renderGroups();
      renderFeed();
    });
    const img = document.createElement("img");
    img.src = sub.thumbnail ?? "";
    label.append(checkbox, img, document.createTextNode(sub.title));
    container.appendChild(label);
  }
}

// --- Feed -------------------------------------------------------------

function parseIsoDuration(iso) {
  const match = /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso ?? "");
  if (!match) return 0;
  const [, h, m, s] = match;
  return (Number(h) || 0) * 3600 + (Number(m) || 0) * 60 + (Number(s) || 0);
}

const SHORT_MAX_SECONDS = 60;

// Live/upcoming status is authoritative and checked first — a livestream can
// report a near-zero or stale duration while it's airing, which would
// otherwise get misread as a Short. Once a stream has ended
// (liveBroadcastContent === "none"), hasLiveStreamingDetails is still true
// for it forever, so it's not used as a live signal on its own — it just
// falls through to the duration check like any past upload.
function classifyVideoType(video, durationSeconds) {
  if (video.liveBroadcastContent === "live" || video.liveBroadcastContent === "upcoming") {
    return "live";
  }
  return durationSeconds <= SHORT_MAX_SECONDS ? "short" : "video";
}

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

  const typeFilter = el("filterType").value;
  const sortBy = el("sortBy").value;
  const hideWatched = el("hideWatched").checked;

  let videos = collectVideos().map((v) => {
    const durationSeconds = parseIsoDuration(v.duration);
    return { ...v, durationSeconds, type: classifyVideoType(v, durationSeconds) };
  });

  if (typeFilter !== "all") videos = videos.filter((v) => v.type === typeFilter);
  if (hideWatched) videos = videos.filter((v) => !watchedIds.has(v.videoId));

  videos.sort((a, b) => {
    if (sortBy === "duration") return b.durationSeconds - a.durationSeconds;
    if (sortBy === "views") return b.viewCount - a.viewCount;
    return new Date(b.publishedAt) - new Date(a.publishedAt);
  });

  for (const video of videos) {
    feed.appendChild(renderVideoCard(video));
  }
}

function renderVideoCard(video) {
  const card = document.createElement("div");
  card.className = "video-card" + (watchedIds.has(video.videoId) ? " watched" : "");

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
  meta.textContent = `${video.type} · ${video.viewCount.toLocaleString()} views`;

  const watchBtn = document.createElement("button");
  watchBtn.textContent = watchedIds.has(video.videoId) ? "Watched" : "Mark watched";
  watchBtn.addEventListener("click", async () => {
    await store.markVideoWatched(video.videoId);
    watchedIds = await store.getWatchedVideoIds();
    renderFeed();
  });

  info.append(title, meta, watchBtn);
  card.append(link, info);
  return card;
}

for (const id of ["filterType", "sortBy", "hideWatched"]) {
  el(id).addEventListener("change", renderFeed);
}

// --- Dead channels -------------------------------------------------------------

function renderDeadChannels() {
  const container = el("deadChannels");
  container.innerHTML = "";
  const dead = Object.entries(subscriptions).filter(([, sub]) => sub.dead);
  if (dead.length === 0) {
    container.textContent = "No dead channels detected.";
    return;
  }
  for (const [channelId, sub] of dead) {
    const row = document.createElement("div");
    row.className = "dead-row";
    row.append(document.createTextNode(sub.title ?? channelId));
    const btn = document.createElement("button");
    btn.textContent = "Unsubscribe";
    btn.addEventListener("click", async () => {
      await send({ type: "UNSUBSCRIBE", subscriptionId: sub.subscriptionId, channelId });
      subscriptions = await store.getSubscriptionsCache();
      renderDeadChannels();
    });
    row.appendChild(btn);
    container.appendChild(row);
  }
}

refreshAuthUI();
