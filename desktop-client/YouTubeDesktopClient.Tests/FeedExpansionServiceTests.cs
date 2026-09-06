// FeedExpansionServiceTests.cs
using System.Net;
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;
using YouTubeDesktopClient.Sync;

file sealed class ExpansionFakeApi : IYouTubeApiClient
{
    // playlistId -> ordered pages to hand back, one per call
    public Dictionary<string, Queue<PlaylistItemsResult>> Pages { get; } = new();
    public Dictionary<string, VideoInfo> Videos { get; } = new();
    public Func<PlaylistItemsResult>? Thrower { get; set; }

    public Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken) =>
        Task.FromResult(new List<SubscriptionEntry>());

    public Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds) =>
        Task.FromResult(new Dictionary<string, ChannelDetails>());

    public Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId,
        string? previousEtag, int maxResults = 15, string? pageToken = null)
    {
        if (Thrower != null) return Task.FromResult(Thrower());
        return Task.FromResult(Pages[uploadsPlaylistId].Dequeue());
    }

    public Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds) =>
        Task.FromResult(videoIds.Where(Videos.ContainsKey).Select(id => Videos[id]).ToList());

    public Task UnsubscribeAsync(string accessToken, string subscriptionId) => Task.CompletedTask;

    public Task<VideoActionState> GetVideoActionStateAsync(string accessToken, string videoId) =>
        Task.FromResult(new VideoActionState("c", "Channel", "none", null));
    public Task RateVideoAsync(string accessToken, string videoId, string rating) => Task.CompletedTask;
    public Task<string> SubscribeAsync(string accessToken, string channelId) => Task.FromResult("sub-new");
    public Task PostCommentAsync(string accessToken, string videoId, string text) => Task.CompletedTask;
}

public class FeedExpansionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public FeedExpansionServiceTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private void SeedChannel(string channelId, string playlistId, string? nextToken, bool complete = false)
    {
        _store.SaveSubscriptionsCache(new()
        {
            [channelId] = new SubscriptionCacheEntry("Chan", null, null, playlistId, false, "\"e\"", "sub",
                nextToken, complete),
        });
    }

    private static VideoInfo Vid(string id, string channelId) =>
        new(id, channelId, $"Video {id}", null, DateTimeOffset.UtcNow.AddDays(-1), "PT2M", 1, "none");

    [Fact]
    public async Task ExpandAsync_ReturnsFalse_WhenChannelHistoryAlreadyComplete()
    {
        SeedChannel("UC1", "UU1", nextToken: null, complete: true);
        var svc = new FeedExpansionService(new ExpansionFakeApi(), _store, () => Task.FromResult<string?>("t"));

        Assert.False(await svc.ExpandAsync(new[] { "UC1" }));
    }

    [Fact]
    public async Task ExpandAsync_AppendsOlderVideos_AndAdvancesCursor()
    {
        SeedChannel("UC1", "UU1", nextToken: "page2");
        var api = new ExpansionFakeApi();
        api.Pages["UU1"] = new(new[] { new PlaylistItemsResult(new() { "old1" }, "\"x\"", false, "page3") });
        api.Videos["old1"] = Vid("old1", "UC1");
        var svc = new FeedExpansionService(api, _store, () => Task.FromResult<string?>("t"));

        var added = await svc.ExpandAsync(new[] { "UC1" });

        Assert.True(added);
        Assert.Equal("old1", _store.GetVideosCache()["UC1"].Single().VideoId);
        Assert.Equal("page3", _store.GetSubscriptionsCache()["UC1"].UploadsNextPageToken);
        Assert.False(_store.GetSubscriptionsCache()["UC1"].HistoryComplete);
    }

    [Fact]
    public async Task ExpandAsync_MarksHistoryComplete_WhenNoNextPageToken()
    {
        SeedChannel("UC1", "UU1", nextToken: "last");
        var api = new ExpansionFakeApi();
        api.Pages["UU1"] = new(new[] { new PlaylistItemsResult(new() { "old1" }, "\"x\"", false, NextPageToken: null) });
        api.Videos["old1"] = Vid("old1", "UC1");
        var svc = new FeedExpansionService(api, _store, () => Task.FromResult<string?>("t"));

        await svc.ExpandAsync(new[] { "UC1" });

        Assert.True(_store.GetSubscriptionsCache()["UC1"].HistoryComplete);
    }

    [Fact]
    public async Task ExpandAsync_StopsAndFlagsQuota_On403QuotaExceeded()
    {
        SeedChannel("UC1", "UU1", nextToken: "page2");
        var api = new ExpansionFakeApi
        {
            Thrower = () => throw new YouTubeApiException(HttpStatusCode.Forbidden,
                "{ \"error\": { \"errors\": [ { \"reason\": \"quotaExceeded\" } ] } }"),
        };
        var svc = new FeedExpansionService(api, _store, () => Task.FromResult<string?>("t"));

        var added = await svc.ExpandAsync(new[] { "UC1" });

        Assert.False(added);
        Assert.True(svc.QuotaExhausted);
        Assert.False(await svc.ExpandAsync(new[] { "UC1" })); // short-circuits now
    }

    [Fact]
    public async Task ExpandAsync_ReturnsFalse_WhenNotSignedIn()
    {
        SeedChannel("UC1", "UU1", nextToken: "page2");
        var svc = new FeedExpansionService(new ExpansionFakeApi(), _store, () => Task.FromResult<string?>(null));

        Assert.False(await svc.ExpandAsync(new[] { "UC1" }));
    }
}
