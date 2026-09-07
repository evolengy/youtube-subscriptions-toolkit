import * as store from "./storage.js";

// groupEditor.js + emojiPicker.js (plain <script>s, first) publish these.
const { listGroups, channelRows, toggleMembership, DEFAULT_GROUP_ICON } = window.YSTGroupEditor;
const { openEmojiPicker } = window.YSTEmoji;

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
  if (filter !== "all" && filter !== "ungrouped" && !groups[filter]) filter = "all";

  renderRail();
  renderList();
}

// --- Rail (group management + filter) ----------------------------------

function setFilter(key) {
  filter = key;
  renderRail();
  renderList();
}

function railEntry(key, label, count, active) {
  const row = document.createElement("button");
  row.type = "button";
  row.className = "rail-row" + (active ? " active" : "");
  const name = document.createElement("span");
  name.className = "rail-name";
  name.textContent = label;
  const c = document.createElement("span");
  c.className = "rail-count";
  c.textContent = count;
  row.append(name, c);
  row.addEventListener("click", () => setFilter(key));
  return row;
}

function renderRail() {
  const totalSubs = Object.values(subscriptions).filter((s) => !s.dead).length;
  const ungrouped = channelRows(subscriptions, groups, { filter: "ungrouped" }).length;

  const filters = el("railFilters");
  filters.replaceChildren(
    railEntry("all", "All", totalSubs, filter === "all"),
    railEntry("ungrouped", "Ungrouped", ungrouped, filter === "ungrouped")
  );

  const list = el("railGroups");
  list.replaceChildren();
  for (const group of listGroups(groups)) {
    list.appendChild(buildGroupRow(group));
  }
}

function buildGroupRow(group) {
  const row = document.createElement("div");
  row.className = "rail-row group" + (filter === group.groupId ? " active" : "");

  const icon = document.createElement("button");
  icon.type = "button";
  icon.className = "rail-icon";
  icon.textContent = group.icon;
  icon.title = "Change icon";
  icon.addEventListener("click", (e) => {
    e.stopPropagation();
    openEmojiPicker(icon, {
      current: groups[group.groupId]?.icon,
      onPick: (char) => store.setGroupIcon(group.groupId, char),
      onClear: () => store.setGroupIcon(group.groupId, ""),
    });
  });

  const name = document.createElement("span");
  name.className = "rail-name";
  name.textContent = group.name;

  const count = document.createElement("span");
  count.className = "rail-count";
  count.textContent = group.count;

  const rename = document.createElement("button");
  rename.type = "button";
  rename.className = "rail-act";
  rename.textContent = "✎";
  rename.title = "Rename";
  rename.addEventListener("click", (e) => {
    e.stopPropagation();
    startRename(group.groupId, row, name);
  });

  const del = document.createElement("button");
  del.type = "button";
  del.className = "rail-act";
  del.textContent = "✕";
  del.title = "Delete group";
  del.addEventListener("click", async (e) => {
    e.stopPropagation();
    if (!confirm(`Delete group "${group.name}"? Channels stay subscribed.`)) return;
    if (filter === group.groupId) filter = "all";
    await store.deleteGroup(group.groupId);
  });

  row.append(icon, name, count, rename, del);
  row.addEventListener("click", () => setFilter(group.groupId));
  return row;
}

function startRename(groupId, row, nameSpan) {
  const input = document.createElement("input");
  input.className = "rail-edit";
  input.value = groups[groupId]?.name ?? "";
  nameSpan.replaceWith(input);
  input.focus();
  input.select();

  let done = false;
  const commit = async () => {
    if (done) return;
    done = true;
    const name = input.value.trim();
    const current = groups[groupId];
    if (name && current && name !== current.name) {
      await store.upsertGroup(groupId, name, current.channelIds);
    } else {
      renderRail(); // nothing to save — just drop the input
    }
  };
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      commit();
    } else if (e.key === "Escape") {
      done = true;
      renderRail();
    }
  });
  input.addEventListener("blur", commit);
  input.addEventListener("click", (e) => e.stopPropagation());
}

el("newGroupBtn").addEventListener("click", () => {
  if (el("newGroupInput")) return; // already editing
  const input = document.createElement("input");
  input.id = "newGroupInput";
  input.className = "rail-edit";
  input.placeholder = "Group name";
  el("newGroupBtn").before(input);
  input.focus();

  let done = false;
  const finish = async (save) => {
    if (done) return;
    done = true;
    const name = input.value.trim();
    input.remove();
    if (save && name) await store.upsertGroup(`g_${Date.now()}`, name, []);
  };
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      finish(true);
    } else if (e.key === "Escape") {
      finish(false);
    }
  });
  input.addEventListener("blur", () => finish(true));
});

// --- Channel list ----------------------------------------------------

function renderList() {
  const rows = channelRows(subscriptions, groups, { query: el("search").value, filter });
  const list = el("list");
  const scroll = list.scrollTop; // keep position while bulk-editing
  list.replaceChildren();
  el("empty").hidden = rows.length > 0;
  for (const row of rows) list.appendChild(buildChannelRow(row));
  list.scrollTop = scroll;
}

function buildChannelRow(row) {
  const wrap = document.createElement("div");
  wrap.className = "channel-row";

  const img = document.createElement("img");
  img.className = "avatar";
  img.src = row.thumbnail || "";
  img.alt = "";
  wrap.appendChild(img);

  const name = document.createElement("a");
  name.className = "channel-name";
  name.href = `https://www.youtube.com/channel/${row.channelId}`;
  name.target = "_blank";
  name.rel = "noreferrer";
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
      await store.upsertGroup(group.groupId, grp.name, next);
    });
    chips.appendChild(chip);
  }
  wrap.appendChild(chips);

  return wrap;
}

el("search").addEventListener("input", renderList);

chrome.storage.onChanged.addListener((changes, area) => {
  if ((area === "sync" && changes.groups) || (area === "local" && changes.subscriptionsCache)) {
    loadAndRender();
  }
});

loadAndRender();
