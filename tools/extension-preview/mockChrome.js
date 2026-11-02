// In-memory Chrome API substitute. Never reads browser profiles or account data.
(() => {
  const listeners = new Set();
  const subscriptionsCache = {};
  const videosCache = {};
  const now = Date.now();
  const thumbnail = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#26394b"/><text x="320" y="185" text-anchor="middle" fill="#dde9f2" font-family="Arial" font-size="36">Synthetic video</text></svg>');
  for (let c = 0; c < 100; c++) {
    const channelId = `channel-${c}`;
    subscriptionsCache[channelId] = { channelId, title: `Demo channel ${c}`, thumbnail };
    videosCache[channelId] = Array.from({ length: 30 }, (_, n) => ({
      videoId: `video-${c}-${n}`, channelId,
      title: n === 1 ? `A deliberately long video title to check two-line truncation and bottom button alignment ${c}` : `Demo video ${c}-${n}`,
      thumbnail, publishedAt: new Date(now - (c * 30 + n + 1) * 60000).toISOString(),
      duration: n % 3 === 0 ? 'PT2M30S' : 'PT18M42S',
      viewCount: 1000 + c * 100 + n, liveBroadcastContent: 'none',
    }));
  }
  const syncData = {
    groups: { tech: { name: 'Technology', icon: '💻', channelIds: Object.keys(subscriptionsCache).slice(0, 50) }, science: { name: 'Science', icon: '🔬', channelIds: Object.keys(subscriptionsCache).slice(50) } },
    watchedVideoIds: ['video-0-0'], notInterestedVideoIds: ['video-0-2'],
    groupLastVisited: {}, guideHidden: {},
    feedBlocklist: { keywords: ['Demo video 0-3'], mutedChannels: [] },
  };
  const localData = { subscriptionsCache, videosCache, lastSyncedAt: now, prevSyncedAt: now - 86400000, likedVideos: { 'video-0-4': videosCache['channel-0'][4] }, notInterestedVideos: { 'video-0-2': videosCache['channel-0'][2] }, logEntries: [] };
  function area(data, name) {
    return {
      async get(keys) {
        if (keys == null) return structuredClone(data);
        const names = typeof keys === 'string' ? [keys] : Array.isArray(keys) ? keys : Object.keys(keys);
        return structuredClone(Object.fromEntries(names.filter(k => k in data).map(k => [k, data[k]])));
      },
      async set(values) {
        const changes = {};
        for (const [key, value] of Object.entries(values)) {
          changes[key] = { oldValue: data[key], newValue: structuredClone(value) };
          data[key] = structuredClone(value);
        }
        queueMicrotask(() => listeners.forEach(listener => listener(changes, name)));
      },
      async remove(key) { delete data[key]; },
    };
  }
  let signedIn = true;
  window.chrome = {
    storage: { sync: area(syncData, 'sync'), local: area(localData, 'local'), onChanged: { addListener(fn) { listeners.add(fn); }, removeListener(fn) { listeners.delete(fn); } } },
    runtime: {
      getURL: file => new URL(file, location.origin).href,
      async sendMessage(message) {
        if (message.type === 'OPEN_GROUPS') { window.open(new URL('groups.html', location.origin).href, '_blank', 'noopener'); return {}; }
        if (message.type === 'GET_AUTH_STATUS') return { signedIn };
        if (message.type === 'SIGN_IN') signedIn = true;
        if (message.type === 'SIGN_OUT') signedIn = false;
        if (message.type === 'REFRESH_ALL') await chrome.storage.local.set({ lastSyncedAt: Date.now() });
        return {};
      },
    },
    tabs: { async create({ url }) { window.open(url, '_blank', 'noopener'); } },
    alarms: { async get() { return { scheduledTime: Date.now() + 2700000 }; } },
  };
  const banner = document.createElement('div');
  banner.textContent = 'TEST STAND · 3,000 synthetic videos · changes stay in this page · no YouTube API';
  banner.style.cssText = 'padding:8px 16px;background:#244536;color:#fff;font:12px Arial';
  const dashboard = document.createElement('a');
  dashboard.href = '/dashboard.html'; dashboard.textContent = ' Dashboard ';
  const overlay = document.createElement('a');
  overlay.href = '/overlay.html'; overlay.textContent = ' YouTube overlay ';
  for (const link of [dashboard, overlay]) { link.style.color = '#fff'; banner.append(link); }
  document.body.prepend(banner);
})();
