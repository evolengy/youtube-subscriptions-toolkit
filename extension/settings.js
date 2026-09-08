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

// Reflect edits made from another tab / device.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.guideHidden) render();
});

render();
