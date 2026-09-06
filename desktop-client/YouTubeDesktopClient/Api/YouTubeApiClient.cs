using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Api;

public class YouTubeApiClient : IYouTubeApiClient, IYouTubeAccountApi, IYouTubePlaylistApi, IYouTubeCommentApi
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

    public async Task<MyChannel?> GetMyChannelAsync(string accessToken)
    {
        var request = BuildRequest(HttpMethod.Get, "channels", accessToken, new()
        {
            ["part"] = "snippet",
            ["mine"] = "true",
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);
        var items = root.GetProperty("items");
        if (items.GetArrayLength() == 0) return null;

        var item = items[0];
        var snippet = item.GetProperty("snippet");
        var thumbnails = snippet.GetProperty("thumbnails");
        string? thumb =
            thumbnails.TryGetProperty("default", out var def) && def.TryGetProperty("url", out var u)
                ? u.GetString() : null;
        string? handle = snippet.TryGetProperty("customUrl", out var cu) ? cu.GetString() : null;

        return new MyChannel(
            item.GetProperty("id").GetString()!,
            snippet.GetProperty("title").GetString() ?? "",
            thumb,
            handle);
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
        // Owner channel + the metadata the native player page renders. snippet
        // carries channelId/channelTitle/description/publishedAt; statistics
        // carries viewCount. One call, same as before — just a wider `part`.
        var ownerReq = BuildRequest(HttpMethod.Get, "videos", accessToken, new()
        {
            ["part"] = "snippet,statistics",
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
        var description = snippet.TryGetProperty("description", out var descProp)
            ? descProp.GetString() ?? "" : "";
        DateTimeOffset? publishedAt = snippet.TryGetProperty("publishedAt", out var pubProp)
            && pubProp.TryGetDateTimeOffset(out var pub) ? pub : null;
        long viewCount = items[0].TryGetProperty("statistics", out var stats)
            && stats.TryGetProperty("viewCount", out var vcProp)
            && long.TryParse(vcProp.GetString(), out var vc) ? vc : 0;

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

        return new VideoActionState(channelId, channelTitle, rating, subscriptionId,
            description, publishedAt, viewCount);
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
        await EnsureOkAsync(response);
    }

    // ---- comments (IYouTubeCommentApi) -------------------------------

    public async Task<CommentPage> ListCommentThreadsAsync(
        string accessToken, string videoId, string? pageToken = null)
    {
        var request = BuildRequest(HttpMethod.Get, "commentThreads", accessToken, new()
        {
            ["part"] = "snippet,replies",
            ["videoId"] = videoId,
            ["maxResults"] = "20",
            ["order"] = "relevance",
            ["pageToken"] = pageToken,
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);

        var threads = new List<CommentThread>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            var snippet = item.GetProperty("snippet");
            var top = ParseComment(snippet.GetProperty("topLevelComment"));
            var previews = new List<CommentInfo>();
            if (item.TryGetProperty("replies", out var replies) &&
                replies.TryGetProperty("comments", out var cs))
                foreach (var c in cs.EnumerateArray()) previews.Add(ParseComment(c));

            threads.Add(new CommentThread(
                item.GetProperty("id").GetString()!,
                top,
                snippet.TryGetProperty("totalReplyCount", out var trc) ? trc.GetInt32() : 0,
                previews));
        }

        return new CommentPage(threads, NextToken(root));
    }

    public async Task<ReplyPage> ListRepliesAsync(string accessToken, string parentId, string? pageToken = null)
    {
        var request = BuildRequest(HttpMethod.Get, "comments", accessToken, new()
        {
            ["part"] = "snippet",
            ["parentId"] = parentId,
            ["maxResults"] = "100",
            ["pageToken"] = pageToken,
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);

        var replies = root.GetProperty("items").EnumerateArray().Select(ParseComment).ToList();
        return new ReplyPage(replies, NextToken(root));
    }

    public async Task ReplyToCommentAsync(string accessToken, string parentId, string text)
    {
        var request = BuildRequest(HttpMethod.Post, "comments", accessToken, new()
        {
            ["part"] = "snippet",
        });
        request.Content = JsonBody(new { snippet = new { parentId, textOriginal = text } });
        using var response = await _http.SendAsync(request);
        await EnsureOkAsync(response);
    }

    private static CommentInfo ParseComment(JsonElement commentOrThread)
    {
        // Accepts either a `comment` resource or a thread's topLevelComment (both
        // wrap the fields in `snippet`).
        var s = commentOrThread.GetProperty("snippet");
        DateTimeOffset published = s.TryGetProperty("publishedAt", out var p)
            && p.TryGetDateTimeOffset(out var dt) ? dt : default;
        // textOriginal is plain; textDisplay is HTML (<br>, <a>). We render text.
        var text = s.TryGetProperty("textOriginal", out var to) ? to.GetString()
                 : s.TryGetProperty("textDisplay", out var td) ? td.GetString() : "";
        return new CommentInfo(
            commentOrThread.GetProperty("id").GetString()!,
            s.TryGetProperty("authorDisplayName", out var a) ? a.GetString() ?? "" : "",
            s.TryGetProperty("authorProfileImageUrl", out var img) ? img.GetString() : null,
            text ?? "",
            s.TryGetProperty("likeCount", out var lc) ? lc.GetInt64() : 0,
            published);
    }

    private static string? NextToken(JsonElement root) =>
        root.TryGetProperty("nextPageToken", out var n) ? n.GetString() : null;

    // ---- playlists (IYouTubePlaylistApi) -----------------------------

    public async Task<List<PlaylistSummary>> ListMyPlaylistsAsync(string accessToken)
    {
        var results = new List<PlaylistSummary>();
        string? pageToken = null;
        do
        {
            var request = BuildRequest(HttpMethod.Get, "playlists", accessToken, new()
            {
                ["part"] = "snippet,contentDetails,status",
                ["mine"] = "true",
                ["maxResults"] = "50",
                ["pageToken"] = pageToken,
            });
            using var response = await _http.SendAsync(request);
            var root = await ReadJsonRootAsync(response);

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.TryGetProperty("thumbnails", out var t) ? t : default;
                results.Add(new PlaylistSummary(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("title").GetString() ?? "",
                    snippet.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    item.TryGetProperty("contentDetails", out var cd) && cd.TryGetProperty("itemCount", out var ic)
                        ? ic.GetInt64() : 0,
                    ThumbUrl(thumbnails),
                    item.TryGetProperty("status", out var s) && s.TryGetProperty("privacyStatus", out var ps)
                        ? ps.GetString() ?? "private" : "private"));
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (pageToken != null);

        return results;
    }

    public async Task<string> CreatePlaylistAsync(
        string accessToken, string title, string? description, string privacy = "private")
    {
        var request = BuildRequest(HttpMethod.Post, "playlists", accessToken, new()
        {
            ["part"] = "snippet,status",
        });
        request.Content = JsonBody(new
        {
            snippet = new { title, description = description ?? "" },
            status = new { privacyStatus = privacy },
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);
        return root.GetProperty("id").GetString()!;
    }

    public async Task UpdatePlaylistAsync(
        string accessToken, string playlistId, string title, string? description)
    {
        var request = BuildRequest(HttpMethod.Put, "playlists", accessToken, new()
        {
            ["part"] = "snippet",
        });
        request.Content = JsonBody(new
        {
            id = playlistId,
            snippet = new { title, description = description ?? "" },
        });
        using var response = await _http.SendAsync(request);
        await EnsureOkAsync(response);
    }

    public async Task DeletePlaylistAsync(string accessToken, string playlistId)
    {
        var request = BuildRequest(HttpMethod.Delete, "playlists", accessToken, new()
        {
            ["id"] = playlistId,
        });
        using var response = await _http.SendAsync(request);
        await EnsureOkAsync(response);
    }

    public async Task<List<PlaylistItemEntry>> ListPlaylistItemsAsync(string accessToken, string playlistId)
    {
        var results = new List<PlaylistItemEntry>();
        string? pageToken = null;
        do
        {
            var request = BuildRequest(HttpMethod.Get, "playlistItems", accessToken, new()
            {
                ["part"] = "snippet,contentDetails",
                ["playlistId"] = playlistId,
                ["maxResults"] = "50",
                ["pageToken"] = pageToken,
            });
            using var response = await _http.SendAsync(request);
            var root = await ReadJsonRootAsync(response);

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var resource = snippet.TryGetProperty("resourceId", out var r) ? r : default;
                // A deleted / private video keeps its playlistItem row but drops
                // most snippet fields — guard every read.
                results.Add(new PlaylistItemEntry(
                    item.GetProperty("id").GetString()!,
                    resource.ValueKind == JsonValueKind.Object && resource.TryGetProperty("videoId", out var vid)
                        ? vid.GetString() ?? "" : "",
                    snippet.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "",
                    snippet.TryGetProperty("thumbnails", out var th) ? ThumbUrl(th) : null,
                    snippet.TryGetProperty("videoOwnerChannelTitle", out var oc) ? oc.GetString() ?? "" : "",
                    snippet.TryGetProperty("position", out var p) ? p.GetInt32() : 0));
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (pageToken != null);

        return results;
    }

    public async Task<string> AddToPlaylistAsync(string accessToken, string playlistId, string videoId)
    {
        var request = BuildRequest(HttpMethod.Post, "playlistItems", accessToken, new()
        {
            ["part"] = "snippet",
        });
        request.Content = JsonBody(new
        {
            snippet = new
            {
                playlistId,
                resourceId = new { kind = "youtube#video", videoId },
            },
        });
        using var response = await _http.SendAsync(request);
        var root = await ReadJsonRootAsync(response);
        return root.GetProperty("id").GetString()!;
    }

    public async Task RemoveFromPlaylistAsync(string accessToken, string playlistItemId)
    {
        var request = BuildRequest(HttpMethod.Delete, "playlistItems", accessToken, new()
        {
            ["id"] = playlistItemId,
        });
        using var response = await _http.SendAsync(request);
        await EnsureOkAsync(response);
    }

    private static string? ThumbUrl(JsonElement thumbnails)
    {
        if (thumbnails.ValueKind != JsonValueKind.Object) return null;
        foreach (var size in new[] { "medium", "default", "high" })
            if (thumbnails.TryGetProperty(size, out var s) && s.TryGetProperty("url", out var u))
                return u.GetString();
        return null;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response)
    {
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
