// Run: node --test feedFilter.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { parseIsoDuration, classifyVideoType, applyFilters } = require("./feedFilter.js");

test("parseIsoDuration", () => {
  assert.equal(parseIsoDuration("PT4M13S"), 253);
  assert.equal(parseIsoDuration("PT1H2M3S"), 3723);
  assert.equal(parseIsoDuration("PT45S"), 45);
  assert.equal(parseIsoDuration(""), 0);
  assert.equal(parseIsoDuration(null), 0);
});

test("classifyVideoType", () => {
  assert.equal(classifyVideoType({ liveBroadcastContent: "live" }, 30), "live");
  assert.equal(classifyVideoType({ liveBroadcastContent: "upcoming" }, 0), "live");
  assert.equal(classifyVideoType({ liveBroadcastContent: "none" }, 60), "short");
  assert.equal(classifyVideoType({ liveBroadcastContent: "none" }, 61), "video");
});

const NOW = Date.now();
const day = 24 * 60 * 60 * 1000;
function vid(over) {
  return {
    videoId: "v",
    channelId: "c",
    title: "Title",
    duration: "PT10M",
    publishedAt: new Date(NOW - day).toISOString(),
    viewCount: 100,
    liveBroadcastContent: "none",
    ...over,
  };
}

test("applyFilters enriches with type + durationSeconds", () => {
  const [r] = applyFilters([vid({ duration: "PT5M" })], {});
  assert.equal(r.durationSeconds, 300);
  assert.equal(r.type, "video");
});

test("applyFilters: type filter", () => {
  const videos = [vid({ videoId: "a", duration: "PT30S" }), vid({ videoId: "b", duration: "PT5M" })];
  const out = applyFilters(videos, { type: "short" });
  assert.deepEqual(out.map((v) => v.videoId), ["a"]);
});

test("applyFilters: hideWatched", () => {
  const videos = [vid({ videoId: "a" }), vid({ videoId: "b" })];
  const out = applyFilters(videos, { hideWatched: true, watchedIds: new Set(["a"]) });
  assert.deepEqual(out.map((v) => v.videoId), ["b"]);
});

test("applyFilters: duration buckets", () => {
  const videos = [
    vid({ videoId: "s", duration: "PT2M" }),
    vid({ videoId: "m", duration: "PT10M" }),
    vid({ videoId: "l", duration: "PT40M" }),
  ];
  assert.deepEqual(applyFilters(videos, { duration: "under4" }).map((v) => v.videoId), ["s"]);
  assert.deepEqual(applyFilters(videos, { duration: "4to20" }).map((v) => v.videoId), ["m"]);
  assert.deepEqual(applyFilters(videos, { duration: "over20" }).map((v) => v.videoId), ["l"]);
});

test("applyFilters: uploadedWithin", () => {
  const videos = [
    vid({ videoId: "recent", publishedAt: new Date(NOW - 2 * 60 * 60 * 1000).toISOString() }),
    vid({ videoId: "old", publishedAt: new Date(NOW - 10 * day).toISOString() }),
  ];
  assert.deepEqual(applyFilters(videos, { uploadedWithin: "today" }).map((v) => v.videoId), ["recent"]);
  assert.deepEqual(applyFilters(videos, { uploadedWithin: "week" }).map((v) => v.videoId), ["recent"]);
  assert.equal(applyFilters(videos, { uploadedWithin: "month" }).length, 2);
});

test("applyFilters: sort", () => {
  const videos = [
    vid({ videoId: "a", viewCount: 10, duration: "PT1M", publishedAt: new Date(NOW - 3 * day).toISOString() }),
    vid({ videoId: "b", viewCount: 30, duration: "PT9M", publishedAt: new Date(NOW - 1 * day).toISOString() }),
    vid({ videoId: "c", viewCount: 20, duration: "PT5M", publishedAt: new Date(NOW - 2 * day).toISOString() }),
  ];
  assert.deepEqual(applyFilters(videos, { sortBy: "date" }).map((v) => v.videoId), ["b", "c", "a"]);
  assert.deepEqual(applyFilters(videos, { sortBy: "views" }).map((v) => v.videoId), ["b", "c", "a"]);
  assert.deepEqual(applyFilters(videos, { sortBy: "duration" }).map((v) => v.videoId), ["b", "c", "a"]);
});

// Spec for the TODO(human) matchesQuery:
test("applyFilters: text query matches title", () => {
  const videos = [vid({ videoId: "a", title: "Rust tutorial" }), vid({ videoId: "b", title: "Go tutorial" })];
  assert.deepEqual(applyFilters(videos, { query: "rust" }).map((v) => v.videoId), ["a"]);
});

test("applyFilters: text query matches channel name", () => {
  const videos = [vid({ videoId: "a", channelId: "c1" }), vid({ videoId: "b", channelId: "c2" })];
  const channelTitleOf = (id) => (id === "c1" ? "Fireship" : "ThePrimeagen");
  assert.deepEqual(
    applyFilters(videos, { query: "prime", channelTitleOf }).map((v) => v.videoId),
    ["b"]
  );
});

test("applyFilters: blank query keeps everything", () => {
  const videos = [vid({ videoId: "a" }), vid({ videoId: "b" })];
  assert.equal(applyFilters(videos, { query: "   " }).length, 2);
});
