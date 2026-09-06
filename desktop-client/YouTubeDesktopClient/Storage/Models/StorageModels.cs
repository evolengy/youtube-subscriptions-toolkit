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
    string? SubscriptionId,
    // Cursor into the channel's uploads playlist for pulling *older* history on
    // demand (FeedExpansionService). Null before the first page has been fetched
    // OR once HistoryComplete is set. The background sync only ever reads the
    // first page, so it fills this in but never consumes it.
    string? UploadsNextPageToken = null,
    bool HistoryComplete = false);

public record VideoInfo(
    string VideoId,
    string ChannelId,
    string Title,
    string? Thumbnail,
    DateTimeOffset PublishedAt,
    string Duration,
    long ViewCount,
    string LiveBroadcastContent);

/// <summary>
/// User-facing preferences (theme, feed behaviour). Kept as one nullable slot on
/// <see cref="SettingsFile"/> so a settings.json written by an older build — which
/// has no "App" property at all — still deserializes; the store substitutes
/// <c>new AppSettings()</c> in that case.
/// </summary>
public record AppSettings(
    string Theme = "System",
    bool AutoExpandFeed = true,
    string Density = "Comfortable",
    // "Embed"    -> the native page: official IFrame player, no web sign-in, no
    //               quota, but YouTube watch history is not written.
    // "FullPage" -> the real youtube.com/watch page in the tab; writes history
    //               when the shared WebView2 profile is signed in.
    string PlaybackMode = "Embed");

internal record SettingsFile(
    Dictionary<string, GroupData> Groups,
    List<string> WatchedVideoIds,
    AppSettings? App = null);

internal record CacheFile(
    Dictionary<string, SubscriptionCacheEntry> SubscriptionsCache,
    Dictionary<string, List<VideoInfo>> VideosCache,
    DateTimeOffset? LastSyncedAt);
