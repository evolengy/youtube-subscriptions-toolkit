// Run: node --test groupNotify.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import { collectNewVideosForNotify } from "./groupNotify.js";

const NOW = Date.now();
const day = 24 * 60 * 60 * 1000;
const at = (daysAgo) => new Date(NOW - daysAgo * day).toISOString();
const v = (id, channelId, over = {}) => ({
  videoId: id,
  channelId,
  title: `Video ${id}`,
  publishedAt: at(0.2),
  ...over,
});

const groups = {
  g1: { name: "Priority", channelIds: ["c1", "c2"], notify: true },
  g2: { name: "Silent", channelIds: ["c3"], notify: false },
};

test("collects fresh videos only for notify groups", () => {
  const oldCache = { c1: [v("old1", "c1", { publishedAt: at(10) })] };
  const newCache = {
    c1: [v("old1", "c1", { publishedAt: at(10) }), v("new1", "c1")],
    c2: [v("new2", "c2")],
    c3: [v("new3", "c3")],
  };
  const out = collectNewVideosForNotify(groups, oldCache, newCache, { since: NOW - 5 * day, now: NOW });
  assert.deepEqual(Object.keys(out), ["g1"]);
  assert.deepEqual(out.g1.map((x) => x.videoId).sort(), ["new1", "new2"]);
});

test("a video already in the old cache is not new", () => {
  const shared = [v("x", "c1")];
  const out = collectNewVideosForNotify(groups, { c1: shared }, { c1: shared }, {
    since: NOW - 5 * day,
    now: NOW,
  });
  assert.deepEqual(out, {});
});

test("since == null (first sync) notifies nothing", () => {
  const out = collectNewVideosForNotify(groups, {}, { c1: [v("a", "c1")] }, { since: null });
  assert.deepEqual(out, {});
});

test("excludes watched / not-interested / muted channel / keyword", () => {
  const newCache = {
    c1: [v("watched", "c1"), v("ni", "c1"), v("kw", "c1", { title: "LIVE stream" }), v("ok", "c1")],
    c2: [v("muted", "c2")],
  };
  const out = collectNewVideosForNotify(groups, {}, newCache, {
    since: NOW - 5 * day,
    now: NOW,
    watchedIds: new Set(["watched"]),
    notInterestedIds: new Set(["ni"]),
    mutedChannelIds: new Set(["c2"]),
    blockedKeywords: ["live stream"],
  });
  assert.deepEqual(out.g1.map((x) => x.videoId), ["ok"]);
});

test("published before the previous sync is not new", () => {
  const out = collectNewVideosForNotify(groups, {}, { c1: [v("stale", "c1", { publishedAt: at(9) })] }, {
    since: NOW - 5 * day,
    now: NOW,
  });
  assert.deepEqual(out, {});
});
