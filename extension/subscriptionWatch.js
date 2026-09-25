// Keeping the subscription cache in step with subscribe / unsubscribe clicks
// made on youtube.com itself (and, via a cheap list diff, anywhere else).
// Background-only ES module, like groupNotify.js.
//
// Pure: no chrome.*, no DOM. Test: node --test subscriptionWatch.test.mjs

// youtube.com's own subscribe button posts to InnerTube (YouTube's internal,
// undocumented web API) at these paths. background.js observes them with
// chrome.webRequest — the network call is far steadier than the button's DOM.
export const SUBSCRIPTION_REQUEST_URLS = [
  "https://www.youtube.com/youtubei/v1/subscription/subscribe*",
  "https://www.youtube.com/youtubei/v1/subscription/unsubscribe*",
];

const ACTION_BY_PATH = {
  "/youtubei/v1/subscription/subscribe": "subscribe",
  "/youtubei/v1/subscription/unsubscribe": "unsubscribe",
};

// Joins webRequest `requestBody.raw[]` chunks ({ bytes: ArrayBuffer }) into text.
export function decodeRawBody(raw) {
  if (!Array.isArray(raw) || raw.length === 0) return "";
  const decoder = new TextDecoder();
  let text = "";
  for (const part of raw) {
    if (part?.bytes) text += decoder.decode(part.bytes, { stream: true });
  }
  return text + decoder.decode();
}

// url + the request's JSON body text → { action, channelIds } or null when the
// URL isn't a subscription endpoint. channelIds is [] when the body can't be
// read — the caller still knows *something* changed and can fall back to a
// full list diff.
export function parseSubscriptionRequest(url, bodyText) {
  let path;
  try {
    path = new URL(url).pathname;
  } catch {
    return null;
  }
  const action = ACTION_BY_PATH[path];
  if (!action) return null;

  let channelIds = [];
  try {
    const ids = JSON.parse(bodyText || "null")?.channelIds;
    if (Array.isArray(ids)) {
      channelIds = ids.filter((id) => typeof id === "string" && /^UC[\w-]{22}$/.test(id));
    }
  } catch {
    // Not JSON — leave channelIds empty.
  }
  return { action, channelIds };
}

// cache: the stored subscriptionsCache ({ channelId: {...} }).
// fresh: fetchAllSubscriptions() output ([{ subscriptionId, channelId, ... }]).
// Returns { added: [freshEntry, ...], removed: [channelId, ...] }.
export function diffSubscriptions(cache, fresh) {
  const known = new Set(Object.keys(cache || {}));
  const current = new Set();
  const added = [];
  for (const sub of fresh || []) {
    current.add(sub.channelId);
    if (!known.has(sub.channelId)) added.push(sub);
  }
  const removed = [...known].filter((id) => !current.has(id));
  return { added, removed };
}

// Drops channels from both caches and from every group's channelIds (a group
// keeping an unsubscribed channel would show a wrong count forever, and the
// channel no longer appears anywhere to un-assign it). Returns new objects plus
// `groupsChanged`, so the caller only writes chrome.storage.sync when needed.
export function removeChannels({ subscriptionsCache, videosCache, groups }, channelIds) {
  const drop = new Set(channelIds);
  const without = (map) =>
    Object.fromEntries(Object.entries(map || {}).filter(([id]) => !drop.has(id)));

  let groupsChanged = false;
  const nextGroups = {};
  for (const [groupId, group] of Object.entries(groups || {})) {
    const ids = group.channelIds ?? [];
    const kept = ids.filter((id) => !drop.has(id));
    if (kept.length !== ids.length) {
      groupsChanged = true;
      nextGroups[groupId] = { ...group, channelIds: kept };
    } else {
      nextGroups[groupId] = group;
    }
  }

  return {
    subscriptionsCache: without(subscriptionsCache),
    videosCache: without(videosCache),
    groups: nextGroups,
    groupsChanged,
  };
}
