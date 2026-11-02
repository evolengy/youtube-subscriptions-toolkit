// Run: node --test subscriptionWatch.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import {
  decodeRawBody,
  parseSubscriptionRequest,
  diffSubscriptions,
  removeChannels,
} from "./subscriptionWatch.js";

const UC1 = "UC" + "a".repeat(22);
const UC2 = "UC" + "b".repeat(22);
const SUB_URL = "https://www.youtube.com/youtubei/v1/subscription/subscribe?prettyPrint=false";
const UNSUB_URL = "https://www.youtube.com/youtubei/v1/subscription/unsubscribe?prettyPrint=false";

const bytes = (s) => new TextEncoder().encode(s).buffer;

test("decodeRawBody joins chunks, tolerates empty input", () => {
  assert.equal(decodeRawBody([{ bytes: bytes('{"a":') }, { bytes: bytes("1}") }]), '{"a":1}');
  assert.equal(decodeRawBody(undefined), "");
  assert.equal(decodeRawBody([]), "");
});

test("decodeRawBody keeps a multi-byte char split across chunks intact", () => {
  const all = new TextEncoder().encode("ё");
  const out = decodeRawBody([{ bytes: all.slice(0, 1).buffer }, { bytes: all.slice(1).buffer }]);
  assert.equal(out, "ё");
});

test("parses subscribe and unsubscribe requests", () => {
  const body = JSON.stringify({ context: {}, channelIds: [UC1], params: "x" });
  assert.deepEqual(parseSubscriptionRequest(SUB_URL, body), { action: "subscribe", channelIds: [UC1] });
  assert.deepEqual(parseSubscriptionRequest(UNSUB_URL, body), {
    action: "unsubscribe",
    channelIds: [UC1],
  });
});

test("ignores unrelated URLs", () => {
  assert.equal(parseSubscriptionRequest("https://www.youtube.com/youtubei/v1/browse", "{}"), null);
  assert.equal(parseSubscriptionRequest("not a url", "{}"), null);
});

test("unreadable body still reports the action with no ids", () => {
  assert.deepEqual(parseSubscriptionRequest(SUB_URL, "garbage"), { action: "subscribe", channelIds: [] });
  assert.deepEqual(parseSubscriptionRequest(SUB_URL, ""), { action: "subscribe", channelIds: [] });
  assert.deepEqual(parseSubscriptionRequest(SUB_URL, '{"channelIds":["nope",42]}'), {
    action: "subscribe",
    channelIds: [],
  });
});

test("diffSubscriptions finds added and removed channels", () => {
  const cache = { [UC1]: { title: "one" }, old: { title: "gone" } };
  const fresh = [
    { channelId: UC1, subscriptionId: "s1" },
    { channelId: UC2, subscriptionId: "s2" },
  ];
  assert.deepEqual(diffSubscriptions(cache, fresh), {
    added: [{ channelId: UC2, subscriptionId: "s2" }],
    removed: ["old"],
  });
  assert.deepEqual(diffSubscriptions({}, []), { added: [], removed: [] });
});

test("removeChannels drops caches but preserves membership for resubscription", () => {
  const input = {
    subscriptionsCache: { a: {}, b: {} },
    videosCache: { a: [1], b: [2] },
    groups: {
      g1: { name: "G1", icon: "🎮", channelIds: ["a", "b"] },
      g2: { name: "G2", channelIds: ["b"] },
    },
  };
  const out = removeChannels(input, ["a"]);
  assert.deepEqual(out.subscriptionsCache, { b: {} });
  assert.deepEqual(out.videosCache, { b: [2] });
  assert.deepEqual(out.groups.g1, input.groups.g1);
  assert.equal(out.groups.g2, input.groups.g2); // untouched group kept as-is
  assert.equal(out.groupsChanged, false);
  assert.deepEqual(input.subscriptionsCache, { a: {}, b: {} }); // input not mutated
});

test("removeChannels reports no group change when none held the channel", () => {
  const out = removeChannels(
    { subscriptionsCache: { a: {} }, videosCache: {}, groups: { g: { channelIds: ["x"] } } },
    ["a"]
  );
  assert.equal(out.groupsChanged, false);
});

test("a temporarily missing subscription returns to its previous groups", () => {
  const groups = { g: { name: "Saved", channelIds: [UC1, UC2] } };
  const cache = { [UC1]: { title: "One" }, [UC2]: { title: "Two" } };
  const fresh = [{ channelId: UC2 }];
  const { removed } = diffSubscriptions(cache, fresh);
  const next = removeChannels({ subscriptionsCache: cache, videosCache: {}, groups }, removed);
  const returning = diffSubscriptions(next.subscriptionsCache, [...fresh, { channelId: UC1 }]);
  assert.deepEqual(returning.added, [{ channelId: UC1 }]);
  assert.deepEqual(next.groups.g.channelIds, [UC1, UC2]);
});
