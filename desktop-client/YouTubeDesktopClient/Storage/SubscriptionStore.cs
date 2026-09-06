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

    // Repointed by ActivateAccount to accounts/{id}/. Not readonly: one store
    // instance follows the active account for the app's lifetime.
    private string _settingsPath;
    private string _cachePath;

    // False after Deactivate() (signed out): reads return defaults, writes are
    // dropped, so the panels go empty without touching the last account's files.
    private bool _bound = true;

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

    /// <summary>
    /// Points the store at one account's folder under
    /// <c>{baseDir}\accounts\{accountId}\</c>. The first call also folds a
    /// pre-accounts flat <c>settings.json</c>/<c>cache.json</c> into that folder
    /// (one-time migration). Idempotent; safe to call on every sign-in.
    /// </summary>
    public void ActivateAccount(string accountId, string baseDir)
    {
        lock (_lock)
        {
            var accountsRoot = Path.Combine(baseDir, "accounts");
            var dir = Path.Combine(accountsRoot, accountId);
            var flatSettings = Path.Combine(baseDir, "settings.json");
            var flatCache = Path.Combine(baseDir, "cache.json");

            var firstAccountEver = !Directory.Exists(accountsRoot);
            Directory.CreateDirectory(dir);

            if (firstAccountEver && File.Exists(flatSettings))
            {
                // First sign-in on this machine: the pre-accounts flat files
                // belong to whoever is signing in now.
                File.Move(flatSettings, Path.Combine(dir, "settings.json"), overwrite: true);
                if (File.Exists(flatCache))
                    File.Move(flatCache, Path.Combine(dir, "cache.json"), overwrite: true);
            }
            else
            {
                // Any flat files still lying around are orphans from an earlier
                // partial migration — the per-account folders are authoritative.
                TryDelete(flatSettings);
                TryDelete(flatCache);
            }
            _settingsPath = Path.Combine(dir, "settings.json");
            _cachePath = Path.Combine(dir, "cache.json");
            _bound = true;
        }
    }

    /// <summary>Signed out: reads return empty, writes are dropped until the next
    /// <see cref="ActivateAccount"/>.</summary>
    public void Deactivate()
    {
        lock (_lock) _bound = false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException ex) { Logger.LogError($"Could not remove orphaned {path}", ex); }
        catch (UnauthorizedAccessException ex) { Logger.LogError($"Could not remove orphaned {path}", ex); }
    }

    private SettingsFile ReadSettings()
    {
        lock (_lock)
            return _bound
                ? ReadJsonFile(_settingsPath, () => new SettingsFile(new(), new()))
                : new SettingsFile(new(), new());
    }

    private void WriteSettings(SettingsFile settings)
    {
        lock (_lock)
        {
            if (!_bound) return;
            WriteJsonFile(_settingsPath, settings);
        }
    }

    private CacheFile ReadCache()
    {
        lock (_lock)
            return _bound
                ? ReadJsonFile(_cachePath, () => new CacheFile(new(), new(), null))
                : new CacheFile(new(), new(), null);
    }

    private void WriteCache(CacheFile cache)
    {
        lock (_lock)
        {
            if (!_bound) return;
            WriteJsonFile(_cachePath, cache);
        }
    }

    /// <summary>Removes the given channel ids from every group — called when the
    /// sync notices they've been unsubscribed on the web.</summary>
    public void PruneChannelsFromGroups(IReadOnlyCollection<string> channelIds)
    {
        if (channelIds.Count == 0) return;
        lock (_lock)
        {
            var settings = ReadSettings();
            var remove = new HashSet<string>(channelIds);
            var pruned = settings.Groups.ToDictionary(
                kv => kv.Key,
                kv => kv.Value with
                {
                    ChannelIds = kv.Value.ChannelIds.Where(c => !remove.Contains(c)).ToList(),
                });
            WriteSettings(settings with { Groups = pruned });
        }
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
