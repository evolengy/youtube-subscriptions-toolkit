using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Storage;

public class SubscriptionStore
{
    private const int MaxWatchedIds = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly string _cachePath;

    public SubscriptionStore(string settingsPath, string cachePath)
    {
        _settingsPath = settingsPath;
        _cachePath = cachePath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    }

    private SettingsFile ReadSettings() =>
        File.Exists(_settingsPath)
            ? JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_settingsPath))!
            : new SettingsFile(new(), new());

    private void WriteSettings(SettingsFile settings) =>
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    private CacheFile ReadCache() =>
        File.Exists(_cachePath)
            ? JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath))!
            : new CacheFile(new(), new(), null);

    private void WriteCache(CacheFile cache) =>
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, JsonOptions));

    public Dictionary<string, GroupData> GetGroups() => ReadSettings().Groups;

    public void SaveGroups(Dictionary<string, GroupData> groups)
    {
        var settings = ReadSettings();
        WriteSettings(settings with { Groups = groups });
    }

    public HashSet<string> GetWatchedVideoIds() => new(ReadSettings().WatchedVideoIds);

    public void MarkVideoWatched(string videoId)
    {
        var settings = ReadSettings();
        var ids = new List<string>(settings.WatchedVideoIds);
        ids.Remove(videoId);
        ids.Add(videoId);
        if (ids.Count > MaxWatchedIds)
            ids = ids.GetRange(ids.Count - MaxWatchedIds, MaxWatchedIds);
        WriteSettings(settings with { WatchedVideoIds = ids });
    }

    public Dictionary<string, SubscriptionCacheEntry> GetSubscriptionsCache() =>
        ReadCache().SubscriptionsCache;

    public void SaveSubscriptionsCache(Dictionary<string, SubscriptionCacheEntry> cache)
    {
        var current = ReadCache();
        WriteCache(current with { SubscriptionsCache = cache });
    }

    public Dictionary<string, List<VideoInfo>> GetVideosCache() => ReadCache().VideosCache;

    public void SaveVideosCache(Dictionary<string, List<VideoInfo>> cache)
    {
        var current = ReadCache();
        WriteCache(current with { VideosCache = cache });
    }

    public DateTimeOffset? GetLastSyncedAt() => ReadCache().LastSyncedAt;

    public void SetLastSyncedAt(DateTimeOffset timestamp)
    {
        var current = ReadCache();
        WriteCache(current with { LastSyncedAt = timestamp });
    }
}
