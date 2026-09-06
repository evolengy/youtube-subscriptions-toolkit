using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;
using YouTubeDesktopClient.Sync;

public class FakeYouTubeApiClient : IYouTubeApiClient
{
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();
    public Dictionary<string, ChannelDetails> Channels { get; set; } = new();
    public Dictionary<string, PlaylistItemsResult> PlaylistResults { get; set; } = new();
    public Dictionary<string, List<VideoInfo>> VideoDetailsByChannel { get; set; } = new();
    public int PlaylistCallCount { get; private set; }

    public Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken) =>
        Task.FromResult(Subscriptions);

    public Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds) =>
        Task.FromResult(channelIds.Where(Channels.ContainsKey).ToDictionary(id => id, id => Channels[id]));

    public string? LastPlaylistPageToken { get; private set; }

    public Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15, string? pageToken = null)
    {
        PlaylistCallCount++;
        LastPlaylistPageToken = pageToken;
        return Task.FromResult(PlaylistResults[uploadsPlaylistId]);
    }

    public Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds) =>
        Task.FromResult(VideoDetailsByChannel.Values.SelectMany(v => v).Where(v => videoIds.Contains(v.VideoId)).ToList());

    public Task UnsubscribeAsync(string accessToken, string subscriptionId) => Task.CompletedTask;

    public Task<VideoActionState> GetVideoActionStateAsync(string accessToken, string videoId) =>
        Task.FromResult(new VideoActionState("c", "Channel", "none", null));
    public Task RateVideoAsync(string accessToken, string videoId, string rating) => Task.CompletedTask;
    public Task<string> SubscribeAsync(string accessToken, string channelId) => Task.FromResult("sub-new");
    public Task PostCommentAsync(string accessToken, string videoId, string text) => Task.CompletedTask;
}

public class BackgroundSyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public BackgroundSyncServiceTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RunOnceAsync_PopulatesSubscriptionsCache()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", "thumb") },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", "US", "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), null, NotModified: false) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        var cache = _store.GetSubscriptionsCache();
        Assert.Equal("Channel One", cache["UC1"].Title);
        Assert.Equal("US", cache["UC1"].Country);
        Assert.False(cache["UC1"].Dead);
    }

    [Fact]
    public async Task RunOnceAsync_MarksChannelDead_WhenMissingFromChannelDetails()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UCgone", "Gone Channel", null) },
            Channels = new(), // empty — channel not found
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.True(_store.GetSubscriptionsCache()["UCgone"].Dead);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsPlaylistCall_WhenAccessTokenUnavailable()
    {
        var api = new FakeYouTubeApiClient();
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>(null));

        await service.RunOnceAsync();

        Assert.Equal(0, api.PlaylistCallCount);
        Assert.Empty(_store.GetSubscriptionsCache());
    }

    [Fact]
    public async Task RunOnceAsync_StoresEtagFromPlaylistResponse_ForNextCycle()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), "\"newetag\"", NotModified: false) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Equal("\"newetag\"", _store.GetSubscriptionsCache()["UC1"].PlaylistEtag);
    }

    [Fact]
    public async Task RunOnceAsync_PreservesExistingVideos_WhenPlaylistNotModified()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Channel One", null, null, "UU1", false, "\"oldetag\"", "sub1"),
        });
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new List<VideoInfo> { new("v1", "UC1", "Old Video", null, DateTimeOffset.UtcNow, "PT1M", 10, "none") },
        });
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), "\"oldetag\"", NotModified: true) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Single(_store.GetVideosCache()["UC1"]);
        Assert.Equal("Old Video", _store.GetVideosCache()["UC1"][0].Title);
    }

    [Fact]
    public async Task RunOnceAsync_SeedsUploadsCursor_FromFirstPageNextPageToken()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), "\"e\"", NotModified: false, NextPageToken: "p2") },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Equal("p2", _store.GetSubscriptionsCache()["UC1"].UploadsNextPageToken);
        Assert.False(_store.GetSubscriptionsCache()["UC1"].HistoryComplete);
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotRewindUploadsCursor_AdvancedByExpansion()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Channel One", null, null, "UU1", false, "\"old\"", "sub1",
                UploadsNextPageToken: "deep-page-7", HistoryComplete: false),
        });
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new() { "v1" }, "\"new\"", NotModified: false, NextPageToken: "p2") },
            VideoDetailsByChannel = new() { ["UC1"] = new() { new("v1", "UC1", "V1", null, DateTimeOffset.UtcNow, "PT1M", 1, "none") } },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Equal("deep-page-7", _store.GetSubscriptionsCache()["UC1"].UploadsNextPageToken);
    }

    [Fact]
    public async Task RunOnceAsync_ReconcilesSubscriptions_ReportsDeltaAndPrunesGroups()
    {
        NotificationCenter.Clear();
        _store.SaveGroups(new()
        {
            ["g1"] = new GroupData("Mix", new() { "UC1", "UCgone" }),
        });

        SubscriptionEntry Sub(string id) => new($"s_{id}", id, id + " name", null);
        ChannelDetails Det(string id) => new(id, null, "UU_" + id);

        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { Sub("UC1"), Sub("UCgone") },
            Channels = new() { ["UC1"] = Det("UC1"), ["UCgone"] = Det("UCgone") },
            PlaylistResults = new()
            {
                ["UU_UC1"] = new PlaylistItemsResult(new(), null, NotModified: false),
                ["UU_UCgone"] = new PlaylistItemsResult(new(), null, NotModified: false),
            },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();                     // first run — no diff reported
        Assert.Equal(0, NotificationCenter.Count);

        api.Subscriptions = new() { Sub("UC1"), Sub("UCnew") };
        api.Channels["UCnew"] = Det("UCnew");
        api.PlaylistResults["UU_UCnew"] = new PlaylistItemsResult(new(), null, NotModified: false);

        await service.RunOnceAsync();                     // second run — UCgone removed, UCnew added

        Assert.True(NotificationCenter.Count > 0);
        Assert.Equal(new[] { "UC1" }, _store.GetGroups()["g1"].ChannelIds);
    }

    [Fact]
    public async Task RunOnceAsync_MergesFreshFirstPageOverExpandedHistory()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Channel One", null, null, "UU1", false, "\"old\"", "sub1",
                UploadsNextPageToken: "p9", HistoryComplete: false),
        });
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { new("older", "UC1", "Older", null, DateTimeOffset.UtcNow.AddYears(-1), "PT1M", 1, "none") },
        });
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new() { "fresh" }, "\"new\"", NotModified: false, NextPageToken: "p2") },
            VideoDetailsByChannel = new() { ["UC1"] = new() { new("fresh", "UC1", "Fresh", null, DateTimeOffset.UtcNow, "PT1M", 1, "none") } },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        var cached = _store.GetVideosCache()["UC1"].Select(v => v.VideoId).ToList();
        Assert.Contains("fresh", cached);
        Assert.Contains("older", cached);
    }
}
