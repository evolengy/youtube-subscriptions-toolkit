import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import * as subscriptionWatch from './subscriptionWatch.js';

// Run the real background handlers with in-memory Chrome/API boundaries.
const source = (await readFile(new URL('./background.js', import.meta.url), 'utf8'))
  .replace(/^import[\s\S]*?from\s+"[^"]+";\r?\n/gm, '');
function harness() {
  const state = {
    subscriptionsCache: { a: { title: 'A' }, b: { title: 'B' } },
    videosCache: { a: [{ videoId: 'a1' }], b: [{ videoId: 'b1' }] },
    groups: { g: { name: 'Saved', channelIds: ['a', 'b'] } },
    openedTabs: [],
  };
  let fresh = [{ channelId: 'b', title: 'B' }];
  const store = {
    getSubscriptionsCache: async () => structuredClone(state.subscriptionsCache),
    getVideosCache: async () => structuredClone(state.videosCache),
    getGroups: async () => structuredClone(state.groups),
    saveGroups: async () => assert.fail('Subscription handlers must not write groups'),
    saveSubscriptionsCache: async value => { state.subscriptionsCache = value; },
    saveVideosCache: async value => { state.videosCache = value; },
    getLastSyncedAt: async () => 1,
    getWatchedVideoIds: async () => new Set(),
    getNotInterestedVideoIds: async () => new Set(),
    getFeedBlocklist: async () => ({ keywords: [], mutedChannels: [] }),
    saveLikedVideos: async () => {}, markSynced: async () => {},
  };
  const event = { addListener() {} };
  const context = vm.createContext({
    ...subscriptionWatch, store,
    api: {
      getAuthToken: async () => 'fake', fetchAllSubscriptions: async () => fresh,
      fetchChannelsDetails: async () => new Map(), fetchLikedVideos: async () => [],
      unsubscribe: async () => {},
    },
    logger: { info() {}, error(message) { assert.fail(message); } },
    chrome: {
      tabs: { create: async ({ url }) => { state.openedTabs.push(url); } },
      runtime: { getURL: p => p, onInstalled: event, onStartup: event, onMessage: event },
      action: { onClicked: event }, notifications: { onClicked: event },
      storage: { onChanged: event }, alarms: { onAlarm: event },
      webRequest: { onBeforeRequest: event, onCompleted: event, onErrorOccurred: event },
    },
    setInterval: () => 1, clearInterval() {}, setTimeout,
  });
  vm.runInContext(source, context);
  return { state, context, setFresh: value => { fresh = value; } };
}

test('group editor requests open the internal page through background tabs API', async () => {
  const h = harness();
  await vm.runInContext('handleMessage({ type: "OPEN_GROUPS" })', h.context);
  assert.deepEqual(h.state.openedTabs, ['groups.html']);
});

for (const operation of [
  'refreshAll()',
  'syncSubscriptionList()',
  'applyWebSubscriptionChange({ action: "unsubscribe", channelIds: ["a"] })',
  'handleMessage({ type: "UNSUBSCRIBE", channelId: "a", subscriptionId: "s-a" })',
]) {
  test(`background preserves groups: ${operation}`, async () => {
    const h = harness();
    await vm.runInContext(operation, h.context);
    assert.equal(Object.hasOwn(h.state.subscriptionsCache, 'a'), false);
    assert.equal(Object.hasOwn(h.state.videosCache, 'a'), false);
    assert.deepEqual(h.state.groups.g.channelIds, ['a', 'b']);
    h.setFresh([{ channelId: 'a', title: 'A' }, { channelId: 'b', title: 'B' }]);
    await vm.runInContext('syncSubscriptionList()', h.context);
    assert.equal(Object.hasOwn(h.state.subscriptionsCache, 'a'), true);
    assert.deepEqual(h.state.groups.g.channelIds, ['a', 'b']);
  });
}
