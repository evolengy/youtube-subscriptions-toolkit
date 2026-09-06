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
    public void GetAppSettings_ReturnsDefaults_WhenFileDoesNotExist()
    {
        var settings = _store.GetAppSettings();

        Assert.Equal("System", settings.Theme);
        Assert.True(settings.AutoExpandFeed);
        Assert.Equal("Comfortable", settings.Density);
    }

    [Fact]
    public void SaveAppSettings_ThenGetAppSettings_RoundTrips()
    {
        _store.SaveAppSettings(new AppSettings("Dark", false, "Compact"));

        var loaded = _store.GetAppSettings();

        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.AutoExpandFeed);
        Assert.Equal("Compact", loaded.Density);
    }

    [Fact]
    public void SaveAppSettings_PreservesExistingGroups()
    {
        _store.SaveGroups(new Dictionary<string, GroupData>
        {
            ["g1"] = new GroupData("Music", new List<string> { "UC1" }),
        });

        _store.SaveAppSettings(new AppSettings("Light"));

        Assert.Equal("Music", _store.GetGroups()["g1"].Name);
        Assert.Equal("Light", _store.GetAppSettings().Theme);
    }

    [Fact]
    public void GetAppSettings_ReturnsDefaults_WhenSettingsFilePredatesTheAppSection()
    {
        // A settings.json written before AppSettings existed: Groups + WatchedVideoIds only.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"),
            "{\"Groups\":{},\"WatchedVideoIds\":[]}");

        var settings = _store.GetAppSettings();

        Assert.Equal("System", settings.Theme);
        Assert.True(settings.AutoExpandFeed);
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

    [Fact]
    public void ActivateAccount_KeepsEachAccountsDataSeparate()
    {
        var store = new SubscriptionStore(
            Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));

        store.ActivateAccount("UC_A", _tempDir);
        store.SaveGroups(new() { ["g1"] = new GroupData("A's group", new() { "x" }) });

        store.ActivateAccount("UC_B", _tempDir);
        Assert.Empty(store.GetGroups());
        store.SaveGroups(new() { ["g2"] = new GroupData("B's group", new()) });

        store.ActivateAccount("UC_A", _tempDir);
        Assert.Equal("A's group", store.GetGroups()["g1"].Name);
    }

    [Fact]
    public void ActivateAccount_MigratesTheFlatLayoutIntoTheFirstAccountFolderOnce()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"),
            "{\"Groups\":{\"g\":{\"Name\":\"Legacy\",\"ChannelIds\":[]}},\"WatchedVideoIds\":[]}");
        File.WriteAllText(Path.Combine(_tempDir, "cache.json"),
            "{\"SubscriptionsCache\":{},\"VideosCache\":{},\"LastSyncedAt\":null}");

        var store = new SubscriptionStore(
            Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
        store.ActivateAccount("UC_first", _tempDir);

        Assert.Equal("Legacy", store.GetGroups()["g"].Name);
        Assert.False(File.Exists(Path.Combine(_tempDir, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "accounts", "UC_first", "settings.json")));
    }

    [Fact]
    public void Deactivate_MakesReadsEmptyAndSwallowsWrites()
    {
        _store.SaveGroups(new() { ["g1"] = new GroupData("kept", new()) });
        _store.Deactivate();

        Assert.Empty(_store.GetGroups());                                    // reads empty while signed out
        _store.SaveGroups(new() { ["g2"] = new GroupData("dropped", new()) }); // swallowed, no throw

        _store.ActivateAccount("UC_A", _tempDir);                            // flat settings.json migrates in
        Assert.True(_store.GetGroups().ContainsKey("g1"));
        Assert.False(_store.GetGroups().ContainsKey("g2"));
    }

    [Fact]
    public void PruneChannelsFromGroups_StripsIdsFromEveryGroup()
    {
        _store.SaveGroups(new()
        {
            ["g1"] = new GroupData("Music", new() { "UC1", "UC2", "UC3" }),
            ["g2"] = new GroupData("News", new() { "UC2", "UC4" }),
        });

        _store.PruneChannelsFromGroups(new[] { "UC2", "UC3" });

        Assert.Equal(new[] { "UC1" }, _store.GetGroups()["g1"].ChannelIds);
        Assert.Equal(new[] { "UC4" }, _store.GetGroups()["g2"].ChannelIds);
    }
}
