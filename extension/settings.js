import * as store from "./storage.js";

// Toggle key -> label + one-line description. Keys match guideDeclutter.js's
// SELECTORS; the content script turns the enabled ones into CSS.
const GUIDE_TOGGLES = [
  ["shorts", "Shorts", "The Shorts entry at the top of the menu."],
  ["subChannels", "Subscription channel list", "The flat list of channels under “Subscriptions” (the link itself stays)."],
  ["you", "“You” section", "History, Playlists, Watch later, Liked videos, Your videos, Downloads."],
  ["explore", "“Explore” section", "Music, Movies, Live."],
  ["moreFromYoutube", "“More from YouTube”", "YouTube Music, YouTube Kids, Premium."],
  ["footer", "Footer links", "About, Press, Terms, Privacy… and Report history."],
];

const el = (id) => document.getElementById(id);

// --- Background sync interval --------------------------------------------

// Mirrors background.js's REFRESH_ALARM — keep in sync. chrome.alarms is
// available from any extension page (not just the service worker) as long as
// the "alarms" permission is declared, so this reads the real alarm directly.
const REFRESH_ALARM = "refresh-subscriptions";

async function renderSyncInterval() {
  const minutes = await store.getSyncIntervalMinutes();
  const select = el("syncInterval");
  select.value = String(minutes);
  // A value that isn't one of the fixed options (hand-edited storage, or a
  // default that later changes) would otherwise leave nothing selected.
  if (select.value !== String(minutes)) {
    const opt = document.createElement("option");
    opt.value = String(minutes);
    opt.textContent = `${minutes} minutes (current)`;
    select.appendChild(opt);
    select.value = String(minutes);
  }
}

function formatRelative(ms) {
  const mins = Math.round(ms / 60000);
  if (mins < 60) return `${mins} min`;
  const hrs = Math.floor(mins / 60);
  const rem = mins % 60;
  return rem ? `${hrs} h ${rem} min` : `${hrs} h`;
}

// Answers "is the auto-refresh actually scheduled, and for when" — the thing
// that's otherwise unverifiable when a scheduled sync seems to never happen.
async function renderNextSync() {
  const readout = el("nextSync");
  const alarm = await chrome.alarms.get(REFRESH_ALARM);
  if (!alarm) {
    readout.textContent = "Not scheduled yet — reopen this page in a moment, or reload the extension.";
    return;
  }
  const ms = alarm.scheduledTime - Date.now();
  const relative = ms <= 0 ? "due any moment" : `in ${formatRelative(ms)}`;
  const absolute = new Date(alarm.scheduledTime).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });
  readout.textContent = `Next automatic check: ${relative} (${absolute})`;
}

el("syncInterval").addEventListener("change", (e) => {
  store.setSyncIntervalMinutes(Number(e.target.value));
  // The actual reschedule happens async in background.js via storage.onChanged
  // — this is a best-effort nudge; the 20s poll below self-corrects regardless.
  setTimeout(renderNextSync, 500);
});

async function render() {
  const hidden = await store.getGuideHidden();
  const list = el("guideToggles");
  list.replaceChildren();

  for (const [key, title, desc] of GUIDE_TOGGLES) {
    const li = document.createElement("li");
    const label = document.createElement("label");

    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = Boolean(hidden[key]);
    box.addEventListener("change", () => store.setGuideHidden({ [key]: box.checked }));

    const text = document.createElement("span");
    const t = document.createElement("span");
    t.className = "t-title";
    t.textContent = title;
    const d = document.createElement("span");
    d.className = "t-desc";
    d.textContent = desc;
    text.append(t, d);

    label.append(box, text);
    li.append(label);
    list.append(li);
  }
}

// --- Feed blocklist -------------------------------------------------------

async function renderBlocklist() {
  const [blocklist, subs] = await Promise.all([
    store.getFeedBlocklist(),
    store.getSubscriptionsCache(),
  ]);

  const box = el("blockKeywords");
  if (document.activeElement !== box) box.value = blocklist.keywords.join("\n");

  const list = el("mutedChannels");
  list.replaceChildren();
  el("noMuted").hidden = blocklist.mutedChannels.length > 0;

  for (const channelId of blocklist.mutedChannels) {
    const li = document.createElement("li");
    const row = document.createElement("div");
    row.className = "muted-row";

    const name = document.createElement("span");
    name.className = "t-title";
    name.textContent = subs[channelId]?.title || channelId;

    const btn = document.createElement("button");
    btn.textContent = "Unmute";
    btn.addEventListener("click", async () => {
      await store.toggleMutedChannel(channelId, false);
      renderBlocklist();
    });

    row.append(name, btn);
    li.append(row);
    list.append(li);
  }
}

el("blockKeywords").addEventListener("change", (e) => {
  store.setBlocklistKeywords(e.target.value.split("\n"));
});

// --- Group notifications -------------------------------------------------

const DEFAULT_GROUP_ICON = "📁";

async function renderNotifyGroups() {
  const groups = await store.getGroups();
  const entries = Object.entries(groups);
  const list = el("notifyGroups");
  list.replaceChildren();
  el("noGroups").hidden = entries.length > 0;

  for (const [groupId, group] of entries) {
    const li = document.createElement("li");
    const label = document.createElement("label");

    const box = document.createElement("input");
    box.type = "checkbox";
    box.checked = Boolean(group.notify);
    box.addEventListener("change", () => store.setGroupNotify(groupId, box.checked));

    const text = document.createElement("span");
    text.className = "t-title";
    text.textContent = `${group.icon || DEFAULT_GROUP_ICON} ${group.name}`;

    label.append(box, text);
    li.append(label);
    list.append(li);
  }
}

// Reflect edits made from another tab / device.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== "sync") return;
  if (changes.guideHidden) render();
  if (changes.feedBlocklist) renderBlocklist();
  if (changes.groups) renderNotifyGroups();
  if (changes.syncIntervalMinutes) {
    renderSyncInterval();
    renderNextSync();
  }
});

render();
renderBlocklist();
renderNotifyGroups();
renderSyncInterval();
renderNextSync();
setInterval(renderNextSync, 20_000); // keeps the countdown honest while the page is open
