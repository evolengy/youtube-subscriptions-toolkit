import * as store from "./storage.js";
import { toast } from "./toast.js";

// channelHealth.js (plain <script>, loaded first) publishes this.
const { buildChannelRows, countByStatus, relativeTime, STATUSES } = window.YSTHealth;

const el = (id) => document.getElementById(id);
const titleCase = (s) => s[0].toUpperCase() + s.slice(1);

let subscriptions = {};
let videosCache = {};
const sort = { column: "status", dir: "asc" };
let statusFilter = "all";

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response?.error) throw new Error(response.error);
  return response;
}

async function loadAndRender() {
  const { signedIn } = await send({ type: "GET_AUTH_STATUS" });
  el("app").hidden = !signedIn;
  el("signedOut").hidden = signedIn;
  el("refreshBtn").hidden = !signedIn;
  if (!signedIn) return;

  [subscriptions, videosCache] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getVideosCache(),
  ]);
  renderSyncStatus();
  renderChips();
  renderTable();
}

async function renderSyncStatus() {
  const ts = await store.getLastSyncedAt();
  el("syncStatus").textContent = ts ? `Synced ${new Date(ts).toLocaleString()}` : "";
}

function renderChips() {
  const counts = countByStatus(subscriptions, videosCache);
  const chips = el("statusChips");
  chips.replaceChildren();

  const makeChip = (key, label) => {
    const button = document.createElement("button");
    button.className = "chip" + (statusFilter === key ? " active" : "");
    if (key !== "all") button.dataset.status = key;
    button.textContent = `${label} ${counts[key] ?? 0}`;
    button.addEventListener("click", () => {
      statusFilter = key;
      renderChips();
      renderTable();
    });
    return button;
  };

  chips.append(makeChip("all", "All"));
  for (const status of STATUSES) chips.append(makeChip(status, titleCase(status)));
}

function renderTable() {
  const rows = buildChannelRows(subscriptions, videosCache, {
    sort,
    statusFilter,
    query: el("searchQuery").value,
  });

  const body = el("channelsBody");
  body.replaceChildren();
  el("empty").hidden = rows.length > 0;
  el("empty").textContent =
    Object.keys(subscriptions).length === 0
      ? "No channels cached yet — hit Refresh now."
      : "No channels match the filter.";

  for (const row of rows) body.appendChild(buildRow(row));

  for (const th of document.querySelectorAll("#channelsTable th[data-column]")) {
    th.dataset.sort = th.dataset.column === sort.column ? sort.dir : "";
  }
}

function buildRow(row) {
  const tr = document.createElement("tr");

  const nameTd = document.createElement("td");
  nameTd.className = "col-name";
  if (row.thumbnail) {
    const img = document.createElement("img");
    img.src = row.thumbnail;
    img.alt = "";
    nameTd.appendChild(img);
  }
  const link = document.createElement("a");
  link.href = `https://www.youtube.com/channel/${row.channelId}`;
  link.target = "_blank";
  link.rel = "noreferrer";
  link.textContent = row.title;
  nameTd.appendChild(link);

  const statusTd = document.createElement("td");
  const dot = document.createElement("span");
  dot.className = "status-dot";
  dot.dataset.status = row.status;
  dot.title = titleCase(row.status);
  statusTd.appendChild(dot);

  const lastTd = document.createElement("td");
  lastTd.className = "col-last";
  lastTd.textContent = row.dead ? "—" : relativeTime(row.lastUploadAt) ?? "unknown";
  if (row.lastUploadAt) lastTd.title = new Date(row.lastUploadAt).toLocaleString();

  const actionTd = document.createElement("td");
  actionTd.className = "col-action";
  if (row.dead && row.subscriptionId) {
    const button = document.createElement("button");
    button.textContent = "Unsubscribe";
    button.addEventListener("click", async () => {
      button.disabled = true;
      try {
        await send({
          type: "UNSUBSCRIBE",
          subscriptionId: row.subscriptionId,
          channelId: row.channelId,
        });
        subscriptions = await store.getSubscriptionsCache();
        renderChips();
        renderTable();
        toast.success(`Unsubscribed from ${row.title}`);
      } catch (err) {
        button.disabled = false;
        console.error(err);
        toast.error(err.message);
      }
    });
    actionTd.appendChild(button);
  }

  tr.append(nameTd, statusTd, lastTd, actionTd);
  return tr;
}

for (const th of document.querySelectorAll("#channelsTable th[data-column]")) {
  th.addEventListener("click", () => {
    const column = th.dataset.column;
    if (sort.column === column) {
      sort.dir = sort.dir === "asc" ? "desc" : "asc";
    } else {
      sort.column = column;
      sort.dir = column === "lastUpload" ? "desc" : "asc";
    }
    renderTable();
  });
}

el("searchQuery").addEventListener("input", renderTable);

el("refreshBtn").addEventListener("click", async () => {
  el("refreshBtn").disabled = true;
  el("syncStatus").textContent = "Refreshing…";
  try {
    await send({ type: "REFRESH_ALL" });
  } catch (err) {
    console.error(err);
    toast.error(err.message);
  }
  el("refreshBtn").disabled = false;
  await loadAndRender();
});

// A background sync writes the local cache — reflect it live.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && (changes.subscriptionsCache || changes.videosCache || changes.lastSyncedAt)) {
    loadAndRender();
  }
});

loadAndRender();
