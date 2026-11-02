const test = require('node:test');
const assert = require('node:assert/strict');
const { formatDuration, formatPublished } = require('./videoPresentation.js');

test('large feeds build only the visible batch and append remaining videos on demand', () => {
  const { renderFeedPage } = require('./videoPresentation.js');
  const originalDocument = global.document;
  const makeElement = () => ({
    children: [], listeners: {},
    appendChild(child) { this.children.push(child); return child; },
    append(...children) { this.children.push(...children); },
    replaceChildren(...children) { this.children = children; },
    addEventListener(name, listener) { this.listeners[name] = listener; },
    setAttribute() {},
  });
  global.document = { createElement: makeElement };
  try {
    const feed = makeElement();
    const videos = Array.from({ length: 3000 }, (_, i) => i);
    const built = [];
    const card = id => { built.push(id); return { id }; };
    renderFeedPage(feed, videos, card, 'test');
    assert.equal(built.length, 60);
    assert.equal(feed.children[0].children.length, 60);
    const more = feed.children[1];
    more.listeners.click();
    assert.equal(built.length, 120);
    assert.equal(new Set(built).size, 120);
    assert.deepEqual(built, videos.slice(0, 120));
    renderFeedPage(feed, [2999], card, 'test');
    assert.deepEqual(feed.children[0].children.map(v => v.id), [2999]);
    assert.equal(feed.children[1].hidden, true);
  } finally {
    global.document = originalDocument;
  }
});

test('duration distinguishes missing metadata, minutes, hours and live streams', () => {
  assert.equal(formatDuration({ durationSeconds: 0 }), '');
  assert.equal(formatDuration({ durationSeconds: 65 }), '1:05');
  assert.equal(formatDuration({ durationSeconds: 3723 }), '1:02:03');
  assert.equal(formatDuration({ durationSeconds: 65, liveBroadcastContent: 'live' }), 'LIVE');
});

test('publication date handles missing, invalid, future and elapsed dates', () => {
  const now = Date.parse('2026-11-02T12:00:00Z');
  assert.equal(formatPublished(null, now), '');
  assert.equal(formatPublished('bad date', now), '');
  assert.equal(formatPublished('2026-11-03T12:00:00Z', now), 'Scheduled');
  assert.equal(formatPublished('2026-11-02T11:59:30Z', now), 'Just now');
  assert.equal(formatPublished('2026-11-02T10:00:00Z', now), '2 hours ago');
  assert.equal(formatPublished('2026-10-31T12:00:00Z', now), '2 days ago');
});
