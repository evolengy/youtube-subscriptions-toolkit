// Run: node --test logger.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import { trimLog } from "./logger.js";

test("trimLog keeps everything when under the cap", () => {
  const e = [1, 2, 3].map((n) => ({ msg: `m${n}` }));
  assert.deepEqual(trimLog(e, 10), e);
});

test("trimLog drops the oldest entries past the cap", () => {
  const e = Array.from({ length: 12 }, (_, i) => ({ msg: `m${i}` }));
  const out = trimLog(e, 5);
  assert.equal(out.length, 5);
  assert.deepEqual(out.map((x) => x.msg), ["m7", "m8", "m9", "m10", "m11"]);
});

test("trimLog at exactly the cap is unchanged", () => {
  const e = Array.from({ length: 5 }, (_, i) => ({ msg: `m${i}` }));
  assert.deepEqual(trimLog(e, 5), e);
});
