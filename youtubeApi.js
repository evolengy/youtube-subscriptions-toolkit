// Thin wrapper around YouTube Data API v3. All calls use the same OAuth
// token (scope: https://www.googleapis.com/auth/youtube) — it covers both
// the private calls (subscriptions.list/delete) and the public ones
// (channels.list, playlistItems.list, videos.list), so there's no separate
// API key to manage.

const API_BASE = "https://www.googleapis.com/youtube/v3";

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
  const res = await fetch(url, {
    method,
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`YouTube API ${path} failed: ${res.status} ${body}`);
  }
  if (res.status === 204) return null;
  return res.json();
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
      found.set(item.id, {
        channelId: item.id,
        country: item.snippet.country ?? null,
        uploadsPlaylistId: item.contentDetails.relatedPlaylists.uploads,
      });
    }
  }
  return found;
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
