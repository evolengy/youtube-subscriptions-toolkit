// Run: node --test guideDeclutter.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { buildGuideCss, SELECTORS } = require("./guideDeclutter.js");

test("buildGuideCss: nothing enabled -> empty string", () => {
  assert.equal(buildGuideCss({}), "");
  assert.equal(buildGuideCss(undefined), "");
  assert.equal(buildGuideCss({ shorts: false, you: false }), "");
});

test("buildGuideCss: one toggle -> its selector + display none", () => {
  const css = buildGuideCss({ you: true });
  assert.ok(css.includes(SELECTORS.you));
  assert.ok(css.includes("display: none !important"));
  assert.ok(!css.includes(SELECTORS.shorts));
});

test("buildGuideCss: multiple toggles are comma-joined into one rule", () => {
  const css = buildGuideCss({ shorts: true, footer: true });
  assert.ok(css.includes(SELECTORS.shorts));
  assert.ok(css.includes(SELECTORS.footer));
  assert.equal((css.match(/display: none/g) || []).length, 1);
  assert.ok(css.includes(","));
});

test("buildGuideCss: unknown keys are ignored", () => {
  assert.equal(buildGuideCss({ bogus: true }), "");
});

test("SELECTORS: every value is a non-empty string keyed by a known toggle", () => {
  const keys = ["shorts", "subChannels", "you", "explore", "moreFromYoutube", "footer"];
  assert.deepEqual(Object.keys(SELECTORS).sort(), [...keys].sort());
  for (const v of Object.values(SELECTORS)) assert.ok(typeof v === "string" && v.length > 0);
});
