using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Api;

public class YouTubeApiClient : IYouTubeApiClient
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

    /// <summary>
    /// Reads a read-path response body, converting any non-success status into
    /// a <see cref="YouTubeApiException"/> instead of letting the error payload
    /// (a Google error object, or an HTML page) blow up inside JSON parsing.
    /// </summary>
    private static async Task<JsonElement> ReadJsonRootAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new YouTubeApiException(response.StatusCode, body);
        return JsonDocument.Parse(body).RootElement;
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
            var root = await ReadJsonRootAsync(response);

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
            var root = await ReadJsonRootAsync(response);

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
        string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15,
        string? pageToken = null)
    {
        var request = BuildRequest(HttpMethod.Get, "playlistItems", accessToken, new()
        {
            ["part"] = "contentDetails",
            ["playlistId"] = uploadsPlaylistId,
            ["maxResults"] = maxResults.ToString(),
            ["pageToken"] = pageToken,
        });
        // Conditional GET only makes sense for the first page — a deeper page is
        // addressed by pageToken and its response carries its own, unrelated etag.
        if (previousEtag != null && pageToken == null)
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(previousEtag));

        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new PlaylistItemsResult(new List<string>(), previousEtag, NotModified: true);

        var root = await ReadJsonRootAsync(response);
        var videoIds = root.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("contentDetails").GetProperty("videoId").GetString()!)
            .ToList();
        var etag = response.Headers.ETag?.ToString();
        var nextPageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        return new PlaylistItemsResult(videoIds, etag, NotModified: false, nextPageToken);
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
            var root = await ReadJsonRootAsync(response);

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");
                var statistics = item.TryGetProperty("statistics", out var s) ? s : default;
                // contentDetails.duration and snippet.liveBroadcastContent can
                // be absent for some video states (e.g. an in-progress or
                // upcoming live stream/premiere) — a real account hit this
                // and crashed the background sync process before these were
                // made defensive.
                var duration = item.TryGetProperty("contentDetails", out var contentDetails)
                    && contentDetails.TryGetProperty("duration", out var durationProp)
                    ? durationProp.GetString() ?? "PT0S"
                    : "PT0S";
                var liveBroadcastContent = snippet.TryGetProperty("liveBroadcastContent", out var liveProp)
                    ? liveProp.GetString() ?? "none"
                    : "none";

                results.Add(new VideoInfo(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("channelId").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    thumbnails.TryGetProperty("medium", out var med) ? med.GetProperty("url").GetString() : null,
                    snippet.GetProperty("publishedAt").GetDateTimeOffset(),
                    duration,
                    statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("viewCount", out var vc)
                        ? long.Parse(vc.GetString()!) : 0,
                    liveBroadcastContent));
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

    public async Task<VideoActionState> GetVideoActionStateAsync(string accessToken, string videoId)
    {
        // Owner channel (snippet has both id and title).
        var ownerReq = BuildRequest(HttpMethod.Get, "videos", accessToken, new()
        {
            ["part"] = "snippet",
            ["id"] = videoId,
        });
        using var ownerResp = await _http.SendAsync(ownerReq);
        var ownerRoot = await ReadJsonRootAsync(ownerResp);
        var items = ownerRoot.GetProperty("items");
        if (items.GetArrayLength() == 0)
            throw new YouTubeApiException(HttpStatusCode.NotFound, $"video {videoId} not found");
        var snippet = items[0].GetProperty("snippet");
        var channelId = snippet.GetProperty("channelId").GetString()!;
        var channelTitle = snippet.GetProperty("channelTitle").GetString() ?? "";

        // Current rating.
        var rateReq = BuildRequest(HttpMethod.Get, "videos/getRating", accessToken, new()
        {
            ["id"] = videoId,
        });
        using var rateResp = await _http.SendAsync(rateReq);
        var rateRoot = await ReadJsonRootAsync(rateResp);
        var ratingItems = rateRoot.GetProperty("items");
        var rating = ratingItems.GetArrayLength() > 0
            ? ratingItems[0].GetProperty("rating").GetString() ?? "none"
            : "none";

        // Existing subscription to the owner, if any.
        var subReq = BuildRequest(HttpMethod.Get, "subscriptions", accessToken, new()
        {
            ["part"] = "id",
            ["forChannelId"] = channelId,
            ["mine"] = "true",
            ["maxResults"] = "1",
        });
        using var subResp = await _http.SendAsync(subReq);
        var subRoot = await ReadJsonRootAsync(subResp);
        var subItems = subRoot.GetProperty("items");
        var subscriptionId = subItems.GetArrayLength() > 0 ? subItems[0].GetProperty("id").GetString() : null;

        return new VideoActionState(channelId, channelTitle, rating, subscriptionId);
    }

    public async Task RateVideoAsync(string accessToken, string videoId, string rating)
    {
        var request = BuildRequest(HttpMethod.Post, "videos/rate", accessToken, new()
        {
            ["id"] = videoId,
            ["rating"] = rating,
        });
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new YouTubeApiException(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<string> SubscribeAsync(string accessToken, string channelId)
    {
        var request = BuildRequest(HttpMethod.Post, "subscriptions", accessToken, new()
        {
            ["part"] = "snippet",
        });
        request.Content = JsonBody(new
        {
            snippet = new { resourceId = new { kind = "youtube#channel", channelId } },
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);
        return root.GetProperty("id").GetString()!;
    }

    public async Task PostCommentAsync(string accessToken, string videoId, string text)
    {
        var request = BuildRequest(HttpMethod.Post, "commentThreads", accessToken, new()
        {
            ["part"] = "snippet",
        });
        request.Content = JsonBody(new
        {
            snippet = new
            {
                videoId,
                topLevelComment = new { snippet = new { textOriginal = text } },
            },
        });
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new YouTubeApiException(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (int i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
