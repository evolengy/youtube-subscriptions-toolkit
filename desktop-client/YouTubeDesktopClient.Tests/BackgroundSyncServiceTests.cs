using Xunit;
using YouTubeDesktopClient.Api;
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

    public Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15)
    {
        PlaylistCallCount++;
        return Task.FromResult(PlaylistResults[uploadsPlaylistId]);
    }

    public Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds) =>
        Task.FromResult(VideoDetailsByChannel.Values.SelectMany(v => v).Where(v => videoIds.Contains(v.VideoId)).ToList());

    public Task UnsubscribeAsync(string accessToken, string subscriptionId) => Task.CompletedTask;
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
}
