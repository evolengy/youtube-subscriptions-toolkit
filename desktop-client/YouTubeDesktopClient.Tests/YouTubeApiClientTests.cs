using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YouTubeDesktopClient.Api;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}

public class YouTubeApiClientTests
{
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task FetchAllSubscriptionsAsync_ParsesSingleFullPage()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "items": [
            { "id": "sub1", "snippet": { "title": "Channel One",
              "resourceId": { "channelId": "UC1" },
              "thumbnails": { "default": { "url": "http://t1" } } } }
          ]
        }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchAllSubscriptionsAsync("token");

        Assert.Single(result);
        Assert.Equal("UC1", result[0].ChannelId);
        Assert.Equal("Channel One", result[0].Title);
        Assert.Equal("sub1", result[0].SubscriptionId);
    }

    [Fact]
    public async Task FetchAllSubscriptionsAsync_FollowsPagination()
    {
        int callCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return JsonResponse("""
                {
                  "nextPageToken": "page2",
                  "items": [
                    { "id": "sub1", "snippet": { "title": "A", "resourceId": { "channelId": "UC1" }, "thumbnails": {} } }
                  ]
                }
                """);
            }
            return JsonResponse("""
            {
              "items": [
                { "id": "sub2", "snippet": { "title": "B", "resourceId": { "channelId": "UC2" }, "thumbnails": {} } }
              ]
            }
            """);
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchAllSubscriptionsAsync("token");

        Assert.Equal(2, callCount);
        Assert.Equal(2, result.Count);
        Assert.Equal("UC2", result[1].ChannelId);
    }

    [Fact]
    public async Task FetchChannelsDetailsAsync_MapsCountryAndUploadsPlaylist()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "items": [
            { "id": "UC1", "snippet": { "country": "US" },
              "contentDetails": { "relatedPlaylists": { "uploads": "UU1" } } }
          ]
        }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchChannelsDetailsAsync("token", new List<string> { "UC1" });

        Assert.Equal("US", result["UC1"].Country);
        Assert.Equal("UU1", result["UC1"].UploadsPlaylistId);
    }

    [Fact]
    public async Task FetchChannelsDetailsAsync_MissingChannelIsOmitted()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{ "items": [] }"""));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchChannelsDetailsAsync("token", new List<string> { "UCdead" });

        Assert.False(result.ContainsKey("UCdead"));
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_SendsIfNoneMatchHeader_WhenEtagProvided()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{ "items": [] }"""));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: "\"abc\"");

        Assert.Equal("\"abc\"", handler.LastRequest!.Headers.IfNoneMatch.ToString());
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_ReturnsNotModified_On304()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: "\"abc\"");

        Assert.True(result.NotModified);
        Assert.Empty(result.VideoIds);
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_ReturnsVideoIdsAndEtag()
    {
        var response = JsonResponse("""{ "items": [ { "contentDetails": { "videoId": "v1" } } ] }""");
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"newetag\"");
        var handler = new FakeHttpMessageHandler(_ => response);
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: null);

        Assert.False(result.NotModified);
        Assert.Equal(new[] { "v1" }, result.VideoIds);
        Assert.Equal("\"newetag\"", result.Etag);
    }

    [Fact]
    public async Task FetchAllSubscriptionsAsync_ThrowsYouTubeApiException_OnErrorStatus()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{ "error": { "message": "Invalid Credentials" } }"""),
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(
            () => client.FetchAllSubscriptionsAsync("token"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("Invalid Credentials", ex.ResponseBody);
    }

    [Fact]
    public async Task FetchVideosDetailsAsync_ThrowsYouTubeApiException_OnHtmlErrorPage()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("<html><body>503</body></html>", Encoding.UTF8, "text/html"),
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(
            () => client.FetchVideosDetailsAsync("token", new List<string> { "v1" }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_ThrowsYouTubeApiException_OnQuotaExceeded()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "error": { "message": "quotaExceeded" } }"""),
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<YouTubeApiException>(
            () => client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: null));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeAsync_SendsDeleteWithSubscriptionId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.UnsubscribeAsync("token", "sub123");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("id=sub123", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task GetMyChannelAsync_MapsTitleThumbAndHandle()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        { "items": [ { "id": "UCme", "snippet": {
            "title": "My Channel", "customUrl": "@me",
            "thumbnails": { "default": { "url": "http://a" } } } } ] }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var me = await client.GetMyChannelAsync("token");

        Assert.Equal("UCme", me!.ChannelId);
        Assert.Equal("My Channel", me.Title);
        Assert.Equal("@me", me.Handle);
        Assert.Equal("http://a", me.ThumbnailUrl);
    }

    [Fact]
    public async Task GetMyChannelAsync_ReturnsNull_WhenAccountHasNoChannel()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{ "items": [] }"""));
        var client = new YouTubeApiClient(new HttpClient(handler));

        Assert.Null(await client.GetMyChannelAsync("token"));
    }

    [Fact]
    public async Task ListMyPlaylistsAsync_ParsesTitleCountAndPrivacy()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        { "items": [ {
            "id": "PL1",
            "snippet": { "title": "Watch queue", "description": "later",
                         "thumbnails": { "medium": { "url": "http://t" } } },
            "contentDetails": { "itemCount": 12 },
            "status": { "privacyStatus": "unlisted" } } ] }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.ListMyPlaylistsAsync("token");

        Assert.Single(result);
        Assert.Equal("Watch queue", result[0].Title);
        Assert.Equal(12, result[0].ItemCount);
        Assert.Equal("unlisted", result[0].Privacy);
        Assert.Equal("http://t", result[0].ThumbnailUrl);
    }

    [Fact]
    public async Task CreatePlaylistAsync_PostsSnippetAndStatus_ReturnsId()
    {
        string? sentBody = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return JsonResponse("""{ "id": "PLnew" }""");
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var id = await client.CreatePlaylistAsync("token", "New list", "desc", "private");

        Assert.Equal("PLnew", id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("\"title\":\"New list\"", sentBody);
        Assert.Contains("\"privacyStatus\":\"private\"", sentBody);
    }

    [Fact]
    public async Task ListPlaylistItemsAsync_MapsItemIdVideoIdAndSurvivesDeletedVideos()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        { "items": [
            { "id": "pli1", "snippet": { "title": "A", "position": 0,
              "videoOwnerChannelTitle": "Chan",
              "resourceId": { "videoId": "v1" } } },
            { "id": "pli2", "snippet": { "title": "Deleted video", "position": 1 } }
        ] }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var items = await client.ListPlaylistItemsAsync("token", "PL1");

        Assert.Equal("pli1", items[0].PlaylistItemId);
        Assert.Equal("v1", items[0].VideoId);
        Assert.Equal("Chan", items[0].ChannelTitle);
        Assert.Equal("", items[1].VideoId); // no resourceId -> empty, no throw
    }

    [Fact]
    public async Task AddToPlaylistAsync_PostsResourceId_ReturnsItemId()
    {
        string? body = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().Result;
            return JsonResponse("""{ "id": "pliNew" }""");
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var id = await client.AddToPlaylistAsync("token", "PL1", "vid9");

        Assert.Equal("pliNew", id);
        Assert.Contains("\"playlistId\":\"PL1\"", body);
        Assert.Contains("\"videoId\":\"vid9\"", body);
    }

    [Fact]
    public async Task RemoveFromPlaylistAsync_DeletesByItemId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.RemoveFromPlaylistAsync("token", "pli7");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("id=pli7", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task DeletePlaylistAsync_Non2xx_ThrowsYouTubeApiException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("nope"),
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        await Assert.ThrowsAsync<YouTubeApiException>(() => client.DeletePlaylistAsync("token", "PL1"));
    }
}
