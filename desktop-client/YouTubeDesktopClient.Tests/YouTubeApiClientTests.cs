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
    public async Task UnsubscribeAsync_SendsDeleteWithSubscriptionId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.UnsubscribeAsync("token", "sub123");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("id=sub123", handler.LastRequest.RequestUri!.Query);
    }
}
