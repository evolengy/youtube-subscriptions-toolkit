// Run: node --test groupCounts.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { countNewPerGroup } = require("./groupCounts.js");

const NOW = Date.now();
const day = 24 * 60 * 60 * 1000;
const at = (daysAgo) => new Date(NOW - daysAgo * day).toISOString();

const videosByChannel = {
  c1: [
    { videoId: "c1-new", publishedAt: at(1) },
    { videoId: "c1-old", publishedAt: at(10) },
  ],
  c2: [
    { videoId: "c2-new1", publishedAt: at(2) },
    { videoId: "c2-new2", publishedAt: at(3) },
  ],
  c3: [{ videoId: "c3-new", publishedAt: at(1) }],
};

const groups = {
  g1: { name: "A", channelIds: ["c1", "c2"] },
  g2: { name: "B", channelIds: ["c3"] },
};

test("countNewPerGroup: counts videos published after lastVisited", () => {
  const out = countNewPerGroup(groups, videosByChannel, {
    lastVisited: { g1: NOW - 5 * day, g2: NOW - 5 * day, __all__: NOW - 5 * day },
    allChannelIds: ["c1", "c2", "c3"],
    now: NOW,
  });
  assert.equal(out.g1, 3); // c1-new, c2-new1, c2-new2 (c1-old is before)
  assert.equal(out.g2, 1);
  assert.equal(out.__all__, 4);
});

test("countNewPerGroup: no timestamp and no fallback -> 0", () => {
  const out = countNewPerGroup(groups, videosByChannel, { lastVisited: {}, now: NOW });
  assert.equal(out.g1, 0);
  assert.equal(out.g2, 0);
  assert.equal(out.__all__, 0);
});

test("countNewPerGroup: never-opened group falls back to fallbackSince", () => {
  const out = countNewPerGroup(groups, videosByChannel, {
    lastVisited: { g1: NOW - 60 * 1000 }, // g1 opened a minute ago
    fallbackSince: NOW - 5 * day,
    allChannelIds: ["c1", "c2", "c3"],
    now: NOW,
  });
  assert.equal(out.g1, 0); // explicit recent timestamp wins over the fallback
  assert.equal(out.g2, 1); // g2 has no timestamp -> fallbackSince -> c3-new
  assert.equal(out.__all__, 4);
});

test("countNewPerGroup: excludes watched and not-interested", () => {
  const out = countNewPerGroup(groups, videosByChannel, {
    lastVisited: { g1: NOW - 5 * day },
    watchedIds: new Set(["c1-new"]),
    notInterestedIds: new Set(["c2-new1"]),
    now: NOW,
  });
  assert.equal(out.g1, 1); // only c2-new2 left
});

test("countNewPerGroup: __all__ uses allChannelIds, ignores future-dated", () => {
  const withFuture = {
    ...videosByChannel,
    c3: [
      { videoId: "c3-new", publishedAt: at(1) },
      { videoId: "c3-future", publishedAt: new Date(NOW + day).toISOString() },
    ],
  };
  const out = countNewPerGroup(groups, withFuture, {
    lastVisited: { __all__: NOW - 5 * day },
    allChannelIds: ["c1", "c2", "c3"],
    now: NOW,
  });
  assert.equal(out.__all__, 4); // c3-future excluded
});

test("countNewPerGroup: recently-visited group shows nothing new", () => {
  const out = countNewPerGroup(groups, videosByChannel, {
    lastVisited: { g1: NOW - 60 * 1000 }, // a minute ago
    now: NOW,
  });
  assert.equal(out.g1, 0);
});
