// Run: node --test channelHealth.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const {
  classifyChannel,
  relativeTime,
  lastUploadAt,
  countByStatus,
  buildChannelRows,
} = require("./channelHealth.js");

const NOW = Date.parse("2026-09-07T12:00:00Z");
const daysAgo = (n) => new Date(NOW - n * 24 * 60 * 60 * 1000).toISOString();
const vids = (...isoDates) => isoDates.map((publishedAt) => ({ publishedAt }));

test("lastUploadAt returns the newest date, or null", () => {
  assert.equal(lastUploadAt(vids(daysAgo(10), daysAgo(3), daysAgo(40))), daysAgo(3));
  assert.equal(lastUploadAt([]), null);
  assert.equal(lastUploadAt(undefined), null);
  assert.equal(lastUploadAt([{ publishedAt: "not a date" }]), null);
});

test("classifyChannel: dead wins over everything", () => {
  const r = classifyChannel({ dead: true, videos: vids(daysAgo(1)) }, NOW);
  assert.equal(r.status, "dead");
  assert.equal(r.lastUploadAt, null);
});

test("classifyChannel: no videos -> unknown", () => {
  assert.equal(classifyChannel({ videos: [] }, NOW).status, "unknown");
  assert.equal(classifyChannel({}, NOW).status, "unknown");
});

test("classifyChannel: activity buckets", () => {
  assert.equal(classifyChannel({ videos: vids(daysAgo(5)) }, NOW).status, "active");
  assert.equal(classifyChannel({ videos: vids(daysAgo(30)) }, NOW).status, "active");
  assert.equal(classifyChannel({ videos: vids(daysAgo(31)) }, NOW).status, "quiet");
  assert.equal(classifyChannel({ videos: vids(daysAgo(180)) }, NOW).status, "quiet");
  assert.equal(classifyChannel({ videos: vids(daysAgo(181)) }, NOW).status, "dormant");
  assert.equal(classifyChannel({ videos: vids(daysAgo(900)) }, NOW).status, "dormant");
});

test("classifyChannel: status + last-upload date come from the newest video", () => {
  const r = classifyChannel({ videos: vids(daysAgo(50), daysAgo(200)) }, NOW);
  assert.equal(r.status, "quiet"); // newest is 50 days old
  assert.equal(r.lastUploadAt, daysAgo(50));
});

test("relativeTime: coarse buckets with pluralisation", () => {
  assert.equal(relativeTime(daysAgo(0), NOW), "just now");
  assert.equal(relativeTime(daysAgo(1), NOW), "1 day ago");
  assert.equal(relativeTime(daysAgo(3), NOW), "3 days ago");
  assert.equal(relativeTime(daysAgo(10), NOW), "1 week ago");
  assert.equal(relativeTime(daysAgo(45), NOW), "1 month ago");
  assert.equal(relativeTime(daysAgo(240), NOW), "8 months ago");
  assert.equal(relativeTime(daysAgo(800), NOW), "2 years ago");
});

test("relativeTime: null / bad input -> null", () => {
  assert.equal(relativeTime(null, NOW), null);
  assert.equal(relativeTime("", NOW), null);
  assert.equal(relativeTime("nonsense", NOW), null);
});

// --- buildChannelRows / countByStatus ---

const SUBS = {
  a: { title: "Zeta", dead: false },
  b: { title: "Alpha", dead: false },
  c: { title: "Gone", dead: true, subscriptionId: "s-c" },
  d: { title: "Beta", dead: false },
};
const CACHE = {
  a: vids(daysAgo(5)), //   active
  b: vids(daysAgo(300)), // dormant
  d: [], //                 unknown
};

test("countByStatus tallies every subscription", () => {
  assert.deepEqual(countByStatus(SUBS, CACHE, NOW), {
    all: 4,
    active: 1,
    quiet: 0,
    dormant: 1,
    dead: 1,
    unknown: 1,
  });
});

test("buildChannelRows: default sort is status severity", () => {
  const rows = buildChannelRows(SUBS, CACHE, {}, NOW);
  assert.deepEqual(rows.map((r) => r.status), ["dead", "dormant", "active", "unknown"]);
});

test("buildChannelRows: sort by title asc / desc", () => {
  const asc = buildChannelRows(SUBS, CACHE, { sort: { column: "title", dir: "asc" } }, NOW);
  assert.deepEqual(asc.map((r) => r.title), ["Alpha", "Beta", "Gone", "Zeta"]);
  const desc = buildChannelRows(SUBS, CACHE, { sort: { column: "title", dir: "desc" } }, NOW);
  assert.deepEqual(desc.map((r) => r.title), ["Zeta", "Gone", "Beta", "Alpha"]);
});

test("buildChannelRows: sort by lastUpload puts unknowns oldest", () => {
  const rows = buildChannelRows(SUBS, CACHE, { sort: { column: "lastUpload", dir: "desc" } }, NOW);
  // newest first: active(5d) > dormant(300d) > dead(null) / unknown(null)
  assert.equal(rows[0].title, "Zeta");
  assert.equal(rows[1].title, "Alpha");
});

test("buildChannelRows: status filter + query", () => {
  assert.deepEqual(
    buildChannelRows(SUBS, CACHE, { statusFilter: "dead" }, NOW).map((r) => r.title),
    ["Gone"]
  );
  assert.deepEqual(
    buildChannelRows(SUBS, CACHE, { query: "et" }, NOW).map((r) => r.title).sort(),
    ["Beta", "Zeta"]
  );
});

test("buildChannelRows: carries dead + subscriptionId for the Unsubscribe action", () => {
  const [row] = buildChannelRows(SUBS, CACHE, { statusFilter: "dead" }, NOW);
  assert.equal(row.dead, true);
  assert.equal(row.subscriptionId, "s-c");
});
