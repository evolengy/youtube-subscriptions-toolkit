using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Storage;

public class SubscriptionStore
{
    private const int MaxWatchedIds = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly string _cachePath;

    // The background sync thread writes while the UI thread reads (the feed
    // and channel panels refresh on SyncCompleted), so every read-modify-write
    // pair has to be serialized. Monitor is reentrant, which is what lets the
    // public methods lock even though the private helpers lock too.
    private readonly object _lock = new();

    public SubscriptionStore(string settingsPath, string cachePath)
    {
        _settingsPath = settingsPath;
        _cachePath = cachePath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    }

    /// <summary>
    /// Reads and deserializes <paramref name="path"/>, falling back to
    /// <paramref name="makeDefault"/> when the file is missing or unparseable.
    /// A half-written or truncated file (crash, power loss) would otherwise
    /// throw on every subsequent launch; the full history is re-fetchable from
    /// the API by design, so treating it as empty is the recoverable choice.
    /// </summary>
    private static T ReadJsonFile<T>(string path, Func<T> makeDefault)
    {
        if (!File.Exists(path)) return makeDefault();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? makeDefault();
        }
        catch (JsonException ex)
        {
            Logger.LogError($"Corrupt JSON in {path}; treating it as empty", ex);
            return makeDefault();
        }
    }

    /// <summary>
    /// Writes via a sibling temp file plus an atomic replace, so an interrupted
    /// write leaves the previous good file intact instead of a truncated one.
    /// </summary>
    private static void WriteJsonFile<T>(string path, T value)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    private SettingsFile ReadSettings()
    {
        lock (_lock)
            return ReadJsonFile(_settingsPath, () => new SettingsFile(new(), new()));
    }

    private void WriteSettings(SettingsFile settings)
    {
        lock (_lock)
            WriteJsonFile(_settingsPath, settings);
    }

    private CacheFile ReadCache()
    {
        lock (_lock)
            return ReadJsonFile(_cachePath, () => new CacheFile(new(), new(), null));
    }

    private void WriteCache(CacheFile cache)
    {
        lock (_lock)
            WriteJsonFile(_cachePath, cache);
    }

    public Dictionary<string, GroupData> GetGroups()
    {
        lock (_lock)
            return ReadSettings().Groups;
    }

    public void SaveGroups(Dictionary<string, GroupData> groups)
    {
        lock (_lock)
        {
            var settings = ReadSettings();
            WriteSettings(settings with { Groups = groups });
        }
    }

    public AppSettings GetAppSettings()
    {
        lock (_lock)
            return ReadSettings().App ?? new AppSettings();
    }

    public void SaveAppSettings(AppSettings appSettings)
    {
        lock (_lock)
        {
            var settings = ReadSettings();
            WriteSettings(settings with { App = appSettings });
        }
    }

    public HashSet<string> GetWatchedVideoIds()
    {
        lock (_lock)
            return new(ReadSettings().WatchedVideoIds);
    }

    public void MarkVideoWatched(string videoId)
    {
        lock (_lock)
        {
            var settings = ReadSettings();
            var ids = new List<string>(settings.WatchedVideoIds);
            ids.Remove(videoId);
            ids.Add(videoId);
            if (ids.Count > MaxWatchedIds)
                ids = ids.GetRange(ids.Count - MaxWatchedIds, MaxWatchedIds);
            WriteSettings(settings with { WatchedVideoIds = ids });
        }
    }

    public Dictionary<string, SubscriptionCacheEntry> GetSubscriptionsCache()
    {
        lock (_lock)
            return ReadCache().SubscriptionsCache;
    }

    public void SaveSubscriptionsCache(Dictionary<string, SubscriptionCacheEntry> cache)
    {
        lock (_lock)
        {
            var current = ReadCache();
            WriteCache(current with { SubscriptionsCache = cache });
        }
    }

    public Dictionary<string, List<VideoInfo>> GetVideosCache()
    {
        lock (_lock)
            return ReadCache().VideosCache;
    }

    public void SaveVideosCache(Dictionary<string, List<VideoInfo>> cache)
    {
        lock (_lock)
        {
            var current = ReadCache();
            WriteCache(current with { VideosCache = cache });
        }
    }

    /// <summary>
    /// Records a page of *older* history for one channel that FeedExpansionService
    /// just fetched: merges the videos into the channel's cached list and moves
    /// its uploads cursor forward (or marks the history exhausted). Both cache
    /// sections are updated under one lock so a concurrent sync can't interleave.
    /// </summary>
    public void AppendChannelHistory(string channelId, IReadOnlyList<VideoInfo> olderVideos,
        string? nextPageToken, bool historyComplete)
    {
        lock (_lock)
        {
            var cache = ReadCache();

            var videos = new Dictionary<string, List<VideoInfo>>(cache.VideosCache);
            videos.TryGetValue(channelId, out var existing);
            videos[channelId] = VideoMerge.Dedup(existing, olderVideos);

            var subs = new Dictionary<string, SubscriptionCacheEntry>(cache.SubscriptionsCache);
            if (subs.TryGetValue(channelId, out var entry))
                subs[channelId] = entry with
                {
                    UploadsNextPageToken = nextPageToken,
                    HistoryComplete = historyComplete,
                };

            WriteCache(cache with { SubscriptionsCache = subs, VideosCache = videos });
        }
    }

    public DateTimeOffset? GetLastSyncedAt()
    {
        lock (_lock)
            return ReadCache().LastSyncedAt;
    }

    public void SetLastSyncedAt(DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            var current = ReadCache();
            WriteCache(current with { LastSyncedAt = timestamp });
        }
    }
}
