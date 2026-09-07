// Run: node --test channelHealth.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { classifyChannel, relativeTime, lastUploadAt } = require("./channelHealth.js");

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
