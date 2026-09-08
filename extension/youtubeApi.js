// Thin wrapper around YouTube Data API v3. All calls use the same OAuth
// token (scope: https://www.googleapis.com/auth/youtube) — it covers both
// the private calls (subscriptions.list/delete) and the public ones
// (channels.list, playlistItems.list, videos.list), so there's no separate
// API key to manage.

const API_BASE = "https://www.googleapis.com/youtube/v3";

// YouTube Data API blips with transient 5xx (and occasionally 429) — a single
// failing request would otherwise abort a whole sync. Retry those a couple of
// times with exponential backoff. 4xx (400/401/403 quota/404) are real and not
// retried.
const RETRY_STATUSES = new Set([429, 500, 502, 503, 504]);
const MAX_ATTEMPTS = 3;

let sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
export function __setSleepForTests(fn) {
  sleep = fn;
}

export async function getAuthToken({ interactive = false } = {}) {
  try {
    const token = await chrome.identity.getAuthToken({ interactive });
    return token?.token ?? token; // MV3 promise form returns {token}, older callback form returns a string
  } catch (err) {
    if (!interactive) return null;
    throw err;
  }
}

export async function signOut() {
  const token = await getAuthToken({ interactive: false });
  if (!token) return;
  await chrome.identity.removeCachedAuthToken({ token });
  await fetch(`https://oauth2.googleapis.com/revoke?token=${token}`, { method: "POST" });
}

async function apiFetch(path, { params = {}, method = "GET", token } = {}) {
  const url = new URL(`${API_BASE}/${path}`);
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null) url.searchParams.set(key, value);
  }

  let lastError;
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    let res;
    try {
      res = await fetch(url, { method, headers: { Authorization: `Bearer ${token}` } });
    } catch (err) {
      lastError = err; // network error — retryable
      if (attempt === MAX_ATTEMPTS) throw err;
      await sleep(400 * 2 ** (attempt - 1));
      continue;
    }

    if (res.ok) {
      if (res.status === 204) return null;
      return res.json();
    }

    const body = await res.text().catch(() => "");
    const error = new Error(`YouTube API ${path} failed: ${res.status} ${body}`);
    if (!RETRY_STATUSES.has(res.status) || attempt === MAX_ATTEMPTS) throw error;
    lastError = error;
    await sleep(400 * 2 ** (attempt - 1));
  }
  throw lastError;
}

// Fetches every page of the caller's subscriptions.
// Returns [{ subscriptionId, channelId, title, thumbnail }]
export async function fetchAllSubscriptions(token) {
  const results = [];
  let pageToken;
  do {
    const data = await apiFetch("subscriptions", {
      token,
      params: {
        part: "snippet",
        mine: "true",
        maxResults: 50,
        pageToken,
      },
    });
    for (const item of data.items ?? []) {
      results.push({
        subscriptionId: item.id,
        channelId: item.snippet.resourceId.channelId,
        title: item.snippet.title,
        thumbnail: item.snippet.thumbnails?.default?.url,
      });
    }
    pageToken = data.nextPageToken;
  } while (pageToken);
  return results;
}

// Looks up channel details (uploads playlist id + country) for up to 50 ids at a time.
// Channels that no longer exist simply won't appear in the response — caller
// treats "missing from result" as "dead channel".
export async function fetchChannelsDetails(token, channelIds) {
  const found = new Map();
  for (let i = 0; i < channelIds.length; i += 50) {
    const batch = channelIds.slice(i, i + 50);
    const data = await apiFetch("channels", {
      token,
      params: {
        part: "snippet,contentDetails",
        id: batch.join(","),
        maxResults: 50,
      },
    });
    for (const item of data.items ?? []) {
      found.set(item.id, mapChannelItem(item));
    }
  }
  return found;
}

// Single-channel lookup for the watch-page location badge, which only has the
// owner link to go on. YouTube dropped the <meta itemprop="channelId"> tag, so
// the content script now passes whatever the owner <a href> gives it: a
// "@handle" (the common case today) or a raw "UC..." id. channels.list accepts
// either via forHandle= / id=, both costing the same 1 quota unit.
// Returns { channelId, country, uploadsPlaylistId } or null if not found.
export async function fetchChannelDetail(token, { handle, channelId } = {}) {
  const params = { part: "snippet,contentDetails", maxResults: 1 };
  if (handle) params.forHandle = handle.replace(/^@/, "");
  else if (channelId) params.id = channelId;
  else return null;

  const data = await apiFetch("channels", { token, params });
  const item = data.items?.[0];
  return item ? mapChannelItem(item) : null;
}

function mapChannelItem(item) {
  return {
    channelId: item.id,
    country: item.snippet.country ?? null,
    uploadsPlaylistId: item.contentDetails.relatedPlaylists.uploads,
  };
}

// Latest N video ids from a channel's uploads playlist.
export async function fetchRecentUploadIds(token, uploadsPlaylistId, maxResults = 15) {
  const data = await apiFetch("playlistItems", {
    token,
    params: { part: "contentDetails", playlistId: uploadsPlaylistId, maxResults },
  });
  return (data.items ?? []).map((item) => item.contentDetails.videoId);
}

// Full metadata (duration, stats, live status) for up to 50 video ids at a time.
export async function fetchVideosDetails(token, videoIds) {
  const results = [];
  for (let i = 0; i < videoIds.length; i += 50) {
    const batch = videoIds.slice(i, i + 50);
    const data = await apiFetch("videos", {
      token,
      params: {
        part: "snippet,contentDetails,statistics,liveStreamingDetails",
        id: batch.join(","),
        maxResults: 50,
      },
    });
    for (const item of data.items ?? []) {
      results.push({
        videoId: item.id,
        channelId: item.snippet.channelId,
        title: item.snippet.title,
        thumbnail: item.snippet.thumbnails?.medium?.url,
        publishedAt: item.snippet.publishedAt,
        duration: item.contentDetails.duration, // ISO 8601, e.g. "PT4M13S"
        viewCount: Number(item.statistics?.viewCount ?? 0),
        liveBroadcastContent: item.snippet.liveBroadcastContent, // "none" | "live" | "upcoming"
        hasLiveStreamingDetails: Boolean(item.liveStreamingDetails),
      });
    }
  }
  return results;
}

export async function unsubscribe(token, subscriptionId) {
  await apiFetch("subscriptions", { token, method: "DELETE", params: { id: subscriptionId } });
}

// The caller's "Liked videos" playlist — the most recent `maxPages * 50` of it.
// Returns [{ videoId, title, thumbnail, channelId, publishedAt }]. Costs 1 unit
// for the channel lookup + 1 per page. Read-only; the plain `youtube` scope
// covers it.
export async function fetchLikedVideos(token, maxPages = 5) {
  const me = await apiFetch("channels", {
    token,
    params: { part: "contentDetails", mine: "true", maxResults: 1 },
  });
  const likesPlaylist = me.items?.[0]?.contentDetails?.relatedPlaylists?.likes;
  if (!likesPlaylist) return [];

  const results = [];
  let pageToken;
  let pages = 0;
  do {
    const data = await apiFetch("playlistItems", {
      token,
      params: {
        part: "snippet,contentDetails",
        playlistId: likesPlaylist,
        maxResults: 50,
        pageToken,
      },
    });
    for (const item of data.items ?? []) {
      const snippet = item.snippet;
      // Private/removed videos still occupy a slot but carry no real snippet.
      if (!snippet || snippet.title === "Private video" || snippet.title === "Deleted video") {
        continue;
      }
      results.push({
        videoId: item.contentDetails.videoId,
        title: snippet.title,
        thumbnail:
          snippet.thumbnails?.medium?.url ?? snippet.thumbnails?.default?.url ?? null,
        channelId: snippet.videoOwnerChannelId ?? null,
        publishedAt: item.contentDetails.videoPublishedAt ?? snippet.publishedAt,
      });
    }
    pageToken = data.nextPageToken;
  } while (pageToken && ++pages < maxPages);

  return results;
}
