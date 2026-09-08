import { getEntries, clearLog, LOG_STORAGE_KEY } from "./logger.js";

const el = (id) => document.getElementById(id);

const fmt = (ts) => {
  const d = new Date(ts);
  return `${d.toLocaleDateString()} ${d.toLocaleTimeString()}`;
};

async function render() {
  const entries = await getEntries();
  const list = el("entries");
  list.replaceChildren();
  el("empty").hidden = entries.length > 0;

  // Newest first.
  for (const entry of [...entries].reverse()) {
    const li = document.createElement("li");
    li.className = `entry ${entry.level}`;

    const time = document.createElement("span");
    time.className = "time";
    time.textContent = fmt(entry.ts);

    const msg = document.createElement("span");
    msg.className = "msg";
    msg.textContent = entry.msg;

    li.append(time, msg);
    list.append(li);
  }
}

el("clearBtn").addEventListener("click", async () => {
  await clearLog();
  render();
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes[LOG_STORAGE_KEY]) render();
});

render();
