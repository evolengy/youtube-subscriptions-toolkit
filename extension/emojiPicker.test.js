// Run: node --test emojiPicker.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const { firstEmoji } = require("./emojiPicker.js");

test("firstEmoji: single emoji unchanged", () => {
  assert.equal(firstEmoji("🎯"), "🎯");
  assert.equal(firstEmoji("🔥"), "🔥");
});

test("firstEmoji: takes only the first of several", () => {
  assert.equal(firstEmoji("🎯🎨🎬"), "🎯");
});

test("firstEmoji: keeps a ZWJ sequence together as one grapheme", () => {
  assert.equal(firstEmoji("👨‍👩‍👧"), "👨‍👩‍👧");
});

test("firstEmoji: trims surrounding whitespace", () => {
  assert.equal(firstEmoji("  🔥 "), "🔥");
});

test("firstEmoji: empty / nullish -> empty string", () => {
  assert.equal(firstEmoji(""), "");
  assert.equal(firstEmoji("   "), "");
  assert.equal(firstEmoji(null), "");
  assert.equal(firstEmoji(undefined), "");
});

test("firstEmoji: latin text returns its first char (caller gates on it)", () => {
  assert.equal(firstEmoji("music"), "m");
});

test("emojiData: every entry is [char, name, keywords] with a non-empty char", () => {
  const categories = require("./emojiData.js");
  const all = categories.flatMap((c) => c.emoji);
  assert.ok(all.length > 200);
  for (const [char, name, keywords] of all) {
    assert.ok(char && char.length <= 8, `bad char: ${JSON.stringify(char)}`);
    assert.equal(typeof name, "string");
    assert.equal(typeof keywords, "string");
  }
});
