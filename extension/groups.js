import * as store from "./storage.js";

// groupEditor.js (plain <script>, first) publishes this.
const { listGroups, channelRows, toggleMembership } = window.YSTGroupEditor;

const el = (id) => document.getElementById(id);

let subscriptions = {};
let groups = {};
let filter = "all"; // "all" | "ungrouped" | groupId

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response?.error) throw new Error(response.error);
  return response;
}

async function loadAndRender() {
  const { signedIn } = await send({ type: "GET_AUTH_STATUS" });
  el("app").hidden = !signedIn;
  el("signedOut").hidden = signedIn;
  if (!signedIn) return;

  [subscriptions, groups] = await Promise.all([
    store.getSubscriptionsCache(),
    store.getGroups(),
  ]);
  // A filter pointing at a since-deleted group falls back to "all".
  if (filter !== "all" && filter !== "ungrouped" && !groups[filter]) filter = "all";

  renderFilters();
  renderList();
}

function renderFilters() {
  const bar = el("filters");
  bar.replaceChildren();

  const chip = (key, label, count) => {
    const button = document.createElement("button");
    button.className = "chip" + (filter === key ? " active" : "");
    button.textContent = count == null ? label : `${label} ${count}`;
    button.addEventListener("click", () => {
      filter = key;
      renderFilters();
      renderList();
    });
    return button;
  };

  const total = Object.values(subscriptions).filter((s) => !s.dead).length;
  bar.append(chip("all", "All", total));
  bar.append(
    chip("ungrouped", "Ungrouped", channelRows(subscriptions, groups, { filter: "ungrouped" }).length)
  );
  for (const group of listGroups(groups)) {
    bar.append(chip(group.groupId, `${group.icon} ${group.name}`, group.count));
  }
}

function renderList() {
  const rows = channelRows(subscriptions, groups, { query: el("search").value, filter });
  const list = el("list");
  const scroll = list.scrollTop; // keep position while bulk-editing
  list.replaceChildren();
  el("empty").hidden = rows.length > 0;
  for (const row of rows) list.appendChild(buildRow(row));
  list.scrollTop = scroll;
}

function buildRow(row) {
  const wrap = document.createElement("div");
  wrap.className = "channel-row";

  const img = document.createElement("img");
  img.className = "avatar";
  img.src = row.thumbnail || "";
  img.alt = "";
  wrap.appendChild(img);

  const name = document.createElement("span");
  name.className = "channel-name";
  name.textContent = row.title;
  wrap.appendChild(name);

  const chips = document.createElement("div");
  chips.className = "group-chips";
  for (const group of listGroups(groups)) {
    const isMember = row.groupIds.includes(group.groupId);
    const chip = document.createElement("button");
    chip.className = "group-chip" + (isMember ? " active" : "");
    chip.textContent = `${group.icon} ${group.name}`;
    chip.addEventListener("click", async () => {
      const grp = groups[group.groupId];
      if (!grp) return;
      const next = toggleMembership(grp.channelIds, row.channelId);
      groups = await store.upsertGroup(group.groupId, grp.name, next);
      renderFilters(); // counts changed
      renderList(); // scroll is preserved; a filtered row may now appear/vanish
    });
    chips.appendChild(chip);
  }
  wrap.appendChild(chips);

  return wrap;
}

el("search").addEventListener("input", renderList);

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "sync" && changes.groups) loadAndRender();
  if (area === "local" && changes.subscriptionsCache) loadAndRender();
});

loadAndRender();
