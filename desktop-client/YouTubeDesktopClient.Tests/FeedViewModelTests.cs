// FeedViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class FeedViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;
    private readonly FeedViewModel _viewModel;

    public FeedViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
        _viewModel = new FeedViewModel(_store);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static VideoInfo MakeVideo(string id, string channelId, DateTimeOffset published, string duration, long views, string live = "none") =>
        new(id, channelId, $"Video {id}", null, published, duration, views, live);

    [Fact]
    public void GetVisibleItems_WithNullGroup_ReturnsAllChannels()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
            ["UC2"] = new() { MakeVideo("v2", "UC2", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        var items = _viewModel.GetVisibleItems(activeGroupId: null);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void GetVisibleItems_WithGroup_OnlyReturnsAssignedChannels()
    {
        _store.SaveGroups(new() { ["g1"] = new GroupData("Music", new List<string> { "UC1" }) });
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
            ["UC2"] = new() { MakeVideo("v2", "UC2", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        var items = _viewModel.GetVisibleItems("g1");

        Assert.Single(items);
        Assert.Equal("v1", items[0].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_FiltersByType()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new()
            {
                MakeVideo("short1", "UC1", DateTimeOffset.UtcNow, "PT30S", 10),
                MakeVideo("long1", "UC1", DateTimeOffset.UtcNow, "PT10M", 10),
            },
        });
        _viewModel.TypeFilter = "short";

        var items = _viewModel.GetVisibleItems(null);

        Assert.Single(items);
        Assert.Equal("short1", items[0].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_SortsByViewsDescending()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new()
            {
                MakeVideo("low", "UC1", DateTimeOffset.UtcNow, "PT2M", 5),
                MakeVideo("high", "UC1", DateTimeOffset.UtcNow, "PT2M", 500),
            },
        });
        _viewModel.SortBy = "views";

        var items = _viewModel.GetVisibleItems(null);

        Assert.Equal("high", items[0].Video.VideoId);
        Assert.Equal("low", items[1].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_HidesWatched_WhenHideWatchedIsTrue()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
        });
        _store.MarkVideoWatched("v1");
        _viewModel.HideWatched = true;

        Assert.Empty(_viewModel.GetVisibleItems(null));
    }

    [Fact]
    public void MarkWatched_UpdatesStoreAndSubsequentQueries()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        _viewModel.MarkWatched("v1");

        Assert.True(_viewModel.GetVisibleItems(null)[0].IsWatched);
    }
}
