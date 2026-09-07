// Run: node --test groupEditor.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { listGroups, channelRows, toggleMembership, DEFAULT_GROUP_ICON } = require("./groupEditor.js");

const SUBS = {
  a: { title: "Zeta", thumbnail: "t-a" },
  b: { title: "Alpha" },
  c: { title: "Gone", dead: true },
  d: { title: "Beta" },
};
const GROUPS = {
  g1: { name: "Music", icon: "🎵", channelIds: ["a", "d"] },
  g2: { name: "Dev", channelIds: ["a"] },
};

test("listGroups: sorted by name, icon falls back to folder, count from channelIds", () => {
  assert.deepEqual(listGroups(GROUPS), [
    { groupId: "g2", name: "Dev", icon: DEFAULT_GROUP_ICON, count: 1 },
    { groupId: "g1", name: "Music", icon: "🎵", count: 2 },
  ]);
  assert.deepEqual(listGroups({}), []);
  assert.deepEqual(listGroups(undefined), []);
});

test("channelRows: excludes dead, sorts by title, resolves group membership", () => {
  const rows = channelRows(SUBS, GROUPS);
  assert.deepEqual(rows.map((r) => r.title), ["Alpha", "Beta", "Zeta"]); // no "Gone"
  const zeta = rows.find((r) => r.title === "Zeta");
  assert.deepEqual(zeta.groupIds.sort(), ["g1", "g2"]);
  assert.equal(zeta.thumbnail, "t-a");
  assert.deepEqual(rows.find((r) => r.title === "Alpha").groupIds, []);
});

test("channelRows: filter by group", () => {
  assert.deepEqual(
    channelRows(SUBS, GROUPS, { filter: "g1" }).map((r) => r.title),
    ["Beta", "Zeta"]
  );
});

test("channelRows: filter ungrouped", () => {
  assert.deepEqual(
    channelRows(SUBS, GROUPS, { filter: "ungrouped" }).map((r) => r.title),
    ["Alpha"]
  );
});

test("channelRows: query filters by title, case-insensitive", () => {
  assert.deepEqual(channelRows(SUBS, GROUPS, { query: "et" }).map((r) => r.title).sort(), [
    "Beta",
    "Zeta",
  ]);
});

test("toggleMembership: adds when absent, removes when present", () => {
  assert.deepEqual(toggleMembership(["a", "b"], "c"), ["a", "b", "c"]);
  assert.deepEqual(toggleMembership(["a", "b"], "a"), ["b"]);
  assert.deepEqual(toggleMembership(undefined, "a"), ["a"]);
});
