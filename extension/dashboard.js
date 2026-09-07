import * as store from "./storage.js";

// Pure helpers loaded by plain <script> tags in dashboard.html, ahead of this
// module: feed filtering (shared with the overlay) and channel-health.
const { applyFilters } = window.YSTFeed;
const { classifyChannel, relativeTime, STATUS_ORDER } = window.YSTHealth;

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
  renderChannels();
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
    label.append(checkbox, img, buildStatusDot(channelId), document.createTextNode(sub.title));
    container.appendChild(label);
  }
}

// --- Channel health -------------------------------------------------------------

function getChannelHealth(channelId) {
  const sub = subscriptions[channelId];
  return classifyChannel({ dead: sub?.dead, videos: videosByChannel[channelId] });
}

const STATUS_LABEL = {
  dead: "Dead",
  active: "Active",
  quiet: "Quiet",
  dormant: "Dormant",
  unknown: "Unknown",
};

// A small coloured dot with the status name as its tooltip.
function buildStatusDot(channelId) {
  const { status } = getChannelHealth(channelId);
  const dot = document.createElement("span");
  dot.className = "status-dot";
  dot.dataset.status = status;
  dot.title = STATUS_LABEL[status];
  return dot;
}

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

for (const id of ["filterType", "filterDuration", "filterUploaded", "sortBy", "hideWatched"]) {
  el(id).addEventListener("change", renderFeed);
}
el("searchQuery").addEventListener("input", renderFeed);

// --- Dead channels -------------------------------------------------------------

function renderChannels() {
  const container = el("channelHealth");
  container.innerHTML = "";

  const entries = Object.entries(subscriptions);
  if (entries.length === 0) {
    container.textContent = "No subscriptions cached yet.";
    return;
  }

  const rows = entries
    .map(([channelId, sub]) => ({ channelId, sub, health: getChannelHealth(channelId) }))
    .sort((a, b) => {
      const order = STATUS_ORDER[a.health.status] - STATUS_ORDER[b.health.status];
      return order !== 0 ? order : (a.sub.title ?? "").localeCompare(b.sub.title ?? "");
    });

  for (const { channelId, sub, health } of rows) {
    const row = document.createElement("div");
    row.className = "channel-row";

    const badge = document.createElement("span");
    badge.className = "status-badge";
    badge.dataset.status = health.status;
    badge.textContent = STATUS_LABEL[health.status];

    const title = document.createElement("span");
    title.className = "channel-title";
    title.textContent = sub.title ?? channelId;

    const detail = document.createElement("span");
    detail.className = "channel-detail";
    const ago = relativeTime(health.lastUploadAt);
    detail.textContent =
      health.status === "dead"
        ? "unavailable"
        : ago
          ? `last upload ${ago}`
          : "no cached uploads";

    row.append(badge, title, detail);

    if (sub.dead) {
      const btn = document.createElement("button");
      btn.textContent = "Unsubscribe";
      btn.addEventListener("click", async () => {
        await send({ type: "UNSUBSCRIBE", subscriptionId: sub.subscriptionId, channelId });
        subscriptions = await store.getSubscriptionsCache();
        renderChannels();
      });
      row.appendChild(btn);
    }

    container.appendChild(row);
  }
}

refreshAuthUI();
