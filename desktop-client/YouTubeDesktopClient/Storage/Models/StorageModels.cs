using System;
using System.Collections.Generic;

namespace YouTubeDesktopClient.Storage.Models;

public record GroupData(string Name, List<string> ChannelIds);

public record SubscriptionCacheEntry(
    string Title,
    string? Thumbnail,
    string? Country,
    string? UploadsPlaylistId,
    bool Dead,
    string? PlaylistEtag,
    string? SubscriptionId);

public record VideoInfo(
    string VideoId,
    string ChannelId,
    string Title,
    string? Thumbnail,
    DateTimeOffset PublishedAt,
    string Duration,
    long ViewCount,
    string LiveBroadcastContent);

internal record SettingsFile(
    Dictionary<string, GroupData> Groups,
    List<string> WatchedVideoIds);

internal record CacheFile(
    Dictionary<string, SubscriptionCacheEntry> SubscriptionsCache,
    Dictionary<string, List<VideoInfo>> VideosCache,
    DateTimeOffset? LastSyncedAt);
