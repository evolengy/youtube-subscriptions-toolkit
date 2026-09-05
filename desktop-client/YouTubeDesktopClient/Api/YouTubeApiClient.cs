using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Api;

public class YouTubeApiClient
{
    private const string ApiBase = "https://www.googleapis.com/youtube/v3";
    private readonly HttpClient _http;

    public YouTubeApiClient(HttpClient httpClient) => _http = httpClient;

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string accessToken,
        Dictionary<string, string?> queryParams)
    {
        var query = string.Join("&", queryParams
            .Where(kv => kv.Value != null)
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        var request = new HttpRequestMessage(method, $"{ApiBase}/{path}?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    public async Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken)
    {
        var results = new List<SubscriptionEntry>();
        string? pageToken = null;
        do
        {
            var request = BuildRequest(HttpMethod.Get, "subscriptions", accessToken, new()
            {
                ["part"] = "snippet",
                ["mine"] = "true",
                ["maxResults"] = "50",
                ["pageToken"] = pageToken,
            });
            using var response = await _http.SendAsync(request);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");
                results.Add(new SubscriptionEntry(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("resourceId").GetProperty("channelId").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    thumbnails.TryGetProperty("default", out var def) ? def.GetProperty("url").GetString() : null));
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (pageToken != null);

        return results;
    }

    public async Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(
        string accessToken, List<string> channelIds)
    {
        var found = new Dictionary<string, ChannelDetails>();
        foreach (var batch in Chunk(channelIds, 50))
        {
            var request = BuildRequest(HttpMethod.Get, "channels", accessToken, new()
            {
                ["part"] = "snippet,contentDetails",
                ["id"] = string.Join(",", batch),
                ["maxResults"] = "50",
            });
            using var response = await _http.SendAsync(request);
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                var snippet = item.GetProperty("snippet");
                var uploads = item.GetProperty("contentDetails").GetProperty("relatedPlaylists").GetProperty("uploads").GetString()!;
                found[id] = new ChannelDetails(
                    id,
                    snippet.TryGetProperty("country", out var c) ? c.GetString() : null,
                    uploads);
            }
        }
        return found;
    }

    public async Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(
        string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15)
    {
        var request = BuildRequest(HttpMethod.Get, "playlistItems", accessToken, new()
        {
            ["part"] = "contentDetails",
            ["playlistId"] = uploadsPlaylistId,
            ["maxResults"] = maxResults.ToString(),
        });
        if (previousEtag != null)
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(previousEtag));

        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new PlaylistItemsResult(new List<string>(), previousEtag, NotModified: true);

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var videoIds = root.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("contentDetails").GetProperty("videoId").GetString()!)
            .ToList();
        var etag = response.Headers.ETag?.ToString();
        return new PlaylistItemsResult(videoIds, etag, NotModified: false);
    }

    public async Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds)
    {
        var results = new List<VideoInfo>();
        foreach (var batch in Chunk(videoIds, 50))
        {
            var request = BuildRequest(HttpMethod.Get, "videos", accessToken, new()
            {
                ["part"] = "snippet,contentDetails,statistics,liveStreamingDetails",
                ["id"] = string.Join(",", batch),
                ["maxResults"] = "50",
            });
            using var response = await _http.SendAsync(request);
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");
                var statistics = item.TryGetProperty("statistics", out var s) ? s : default;
                results.Add(new VideoInfo(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("channelId").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    thumbnails.TryGetProperty("medium", out var med) ? med.GetProperty("url").GetString() : null,
                    snippet.GetProperty("publishedAt").GetDateTimeOffset(),
                    item.GetProperty("contentDetails").GetProperty("duration").GetString()!,
                    statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("viewCount", out var vc)
                        ? long.Parse(vc.GetString()!) : 0,
                    snippet.GetProperty("liveBroadcastContent").GetString()!));
            }
        }
        return results;
    }

    public async Task UnsubscribeAsync(string accessToken, string subscriptionId)
    {
        var request = BuildRequest(HttpMethod.Delete, "subscriptions", accessToken, new()
        {
            ["id"] = subscriptionId,
        });
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (int i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
