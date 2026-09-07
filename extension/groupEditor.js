// Pure helpers for the group editor page (groups.html). No DOM, no chrome.* —
// runnable under `node --test groupEditor.test.js`. Publishes a global.

(function (root) {
  const DEFAULT_GROUP_ICON = "📁";

  // [{ groupId, name, icon, count }] sorted by name.
  function listGroups(groups) {
    return Object.entries(groups || {})
      .map(([groupId, group]) => ({
        groupId,
        name: group.name ?? groupId,
        icon: group.icon || DEFAULT_GROUP_ICON,
        count: (group.channelIds || []).length,
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  // One row per non-dead subscription, filtered + sorted by title.
  //   opts.query  - case-insensitive substring on the channel title
  //   opts.filter - "all" | "ungrouped" | a groupId
  // Each row: { channelId, title, thumbnail, groupIds: string[] }.
  function channelRows(subscriptions, groups, opts = {}) {
    const { query = "", filter = "all" } = opts;
    const needle = query.trim().toLowerCase();
    const groupMap = groups || {};
    const groupIdsOf = (channelId) =>
      Object.keys(groupMap).filter((gid) =>
        (groupMap[gid].channelIds || []).includes(channelId)
      );

    let rows = Object.entries(subscriptions || {})
      .filter(([, sub]) => !sub.dead)
      .map(([channelId, sub]) => ({
        channelId,
        title: sub.title ?? channelId,
        thumbnail: sub.thumbnail ?? null,
        groupIds: groupIdsOf(channelId),
      }));

    if (filter === "ungrouped") rows = rows.filter((r) => r.groupIds.length === 0);
    else if (filter !== "all") rows = rows.filter((r) => r.groupIds.includes(filter));

    if (needle) rows = rows.filter((r) => r.title.toLowerCase().includes(needle));

    rows.sort((a, b) => a.title.localeCompare(b.title));
    return rows;
  }

  // Returns the channelIds a toggle would produce (add if absent, remove if present).
  function toggleMembership(channelIds, channelId) {
    const list = channelIds || [];
    return list.includes(channelId)
      ? list.filter((id) => id !== channelId)
      : [...list, channelId];
  }

  const api = { DEFAULT_GROUP_ICON, listGroups, channelRows, toggleMembership };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  root.YSTGroupEditor = api;
})(typeof window !== "undefined" ? window : globalThis);
