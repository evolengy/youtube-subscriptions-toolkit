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
});

render();
renderBlocklist();
renderNotifyGroups();
