using System.Collections.Generic;
using Xunit;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class SubscriptionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public SubscriptionStoreTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(
            Path.Combine(_tempDir, "settings.json"),
            Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetGroups_ReturnsEmptyDictionary_WhenFileDoesNotExist()
    {
        Assert.Empty(_store.GetGroups());
    }

    [Fact]
    public void SaveGroups_ThenGetGroups_RoundTrips()
    {
        var groups = new Dictionary<string, GroupData>
        {
            ["g1"] = new GroupData("Music", new List<string> { "UC1", "UC2" }),
        };

        _store.SaveGroups(groups);
        var loaded = _store.GetGroups();

        Assert.Single(loaded);
        Assert.Equal("Music", loaded["g1"].Name);
        Assert.Equal(new[] { "UC1", "UC2" }, loaded["g1"].ChannelIds);
    }

    [Fact]
    public void MarkVideoWatched_AddsToWatchedSet()
    {
        _store.MarkVideoWatched("v1");
        _store.MarkVideoWatched("v2");

        var watched = _store.GetWatchedVideoIds();

        Assert.Contains("v1", watched);
        Assert.Contains("v2", watched);
    }

    [Fact]
    public void MarkVideoWatched_CapsListAtTwoThousand()
    {
        for (int i = 0; i < 2005; i++)
            _store.MarkVideoWatched($"v{i}");

        Assert.Equal(2000, _store.GetWatchedVideoIds().Count);
        Assert.DoesNotContain("v0", _store.GetWatchedVideoIds());
        Assert.Contains("v2004", _store.GetWatchedVideoIds());
    }

    [Fact]
    public void SubscriptionsCache_RoundTrips()
    {
        var cache = new Dictionary<string, SubscriptionCacheEntry>
        {
            ["UC1"] = new SubscriptionCacheEntry("Some Channel", "http://thumb", "US", "UUxyz", false, "etag123", "sub1"),
        };

        _store.SaveSubscriptionsCache(cache);
        var loaded = _store.GetSubscriptionsCache();

        Assert.Equal("Some Channel", loaded["UC1"].Title);
        Assert.Equal("etag123", loaded["UC1"].PlaylistEtag);
    }

    [Fact]
    public void LastSyncedAt_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        _store.SetLastSyncedAt(now);

        Assert.Equal(now, _store.GetLastSyncedAt());
    }

    [Fact]
    public void GetLastSyncedAt_ReturnsNull_WhenNeverSet()
    {
        Assert.Null(_store.GetLastSyncedAt());
    }

    [Fact]
    public void GetGroups_ReturnsEmpty_WhenSettingsFileIsCorrupt()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{ this is not json");

        Assert.Empty(_store.GetGroups());
    }

    [Fact]
    public void GetSubscriptionsCache_ReturnsEmpty_WhenCacheFileIsCorrupt()
    {
        File.WriteAllText(Path.Combine(_tempDir, "cache.json"), "{ truncated");

        Assert.Empty(_store.GetSubscriptionsCache());
        Assert.Null(_store.GetLastSyncedAt());
    }

    [Fact]
    public void Write_OverCorruptFile_RecoversAndRoundTrips()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not json at all");

        _store.SaveGroups(new Dictionary<string, GroupData>
        {
            ["g1"] = new GroupData("Recovered", new List<string>()),
        });

        Assert.Equal("Recovered", _store.GetGroups()["g1"].Name);
    }

    [Fact]
    public void Write_LeavesNoTempFileBehind()
    {
        _store.SaveGroups(new Dictionary<string, GroupData>());
        _store.SetLastSyncedAt(DateTimeOffset.UtcNow);

        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}
