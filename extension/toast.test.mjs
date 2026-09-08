// Run: node --test toast.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import { summarize } from "./toast.js";

test("summarize keeps a short single-line message as-is", () => {
  assert.equal(summarize("Signed in"), "Signed in");
});

test("summarize takes only the first line", () => {
  assert.equal(summarize("Sync failed\n{\n  big json\n}"), "Sync failed");
});

test("summarize truncates a long first line with an ellipsis", () => {
  const long = "YouTube API videos failed: 500 " + "x".repeat(300);
  const out = summarize(long, 40);
  assert.equal(out.length, 40);
  assert.ok(out.endsWith("…"));
});

test("summarize trims surrounding whitespace", () => {
  assert.equal(summarize("  hello  "), "hello");
});
