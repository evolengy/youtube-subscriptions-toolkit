// Feed/FeedViewModel.cs
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Feed;

public record FeedItem(VideoInfo Video, string Type, int DurationSeconds, bool IsWatched);

public class FeedViewModel
{
    private readonly SubscriptionStore _store;

    public string TypeFilter { get; set; } = "all";
    public string SortBy { get; set; } = "date";
    public bool HideWatched { get; set; }

    // Snapshot of the subscriptions cache taken once per GetVisibleItems call,
    // so per-card country lookups don't re-read and re-deserialize cache.json
    // once for every video on screen.
    private Dictionary<string, SubscriptionCacheEntry>? _channelsSnapshot;

    public FeedViewModel(SubscriptionStore store) => _store = store;

    public List<FeedItem> GetVisibleItems(string? activeGroupId)
    {
        var videosCache = _store.GetVideosCache();
        var watchedIds = _store.GetWatchedVideoIds();
        _channelsSnapshot = _store.GetSubscriptionsCache();

        IEnumerable<string> channelIds = activeGroupId == null
            ? videosCache.Keys
            : _store.GetGroups().TryGetValue(activeGroupId, out var group) ? group.ChannelIds : Enumerable.Empty<string>();

        var items = channelIds
            .Where(videosCache.ContainsKey)
            .SelectMany(channelId => videosCache[channelId])
            .Select(video =>
            {
                var durationSeconds = VideoClassifier.ParseIsoDuration(video.Duration);
                var type = VideoClassifier.ClassifyVideoType(video.LiveBroadcastContent, durationSeconds);
                return new FeedItem(video, type, durationSeconds, watchedIds.Contains(video.VideoId));
            });

        if (TypeFilter != "all")
            items = items.Where(i => i.Type == TypeFilter);
        if (HideWatched)
            items = items.Where(i => !i.IsWatched);

        items = SortBy switch
        {
            "duration" => items.OrderByDescending(i => i.DurationSeconds),
            "views" => items.OrderByDescending(i => i.Video.ViewCount),
            _ => items.OrderByDescending(i => i.Video.PublishedAt),
        };

        return items.ToList();
    }

    /// <summary>
    /// Country lives on the channel's cache entry rather than on the video, so
    /// the feed card has to look it up by channel id. Served from the snapshot
    /// taken by the most recent <see cref="GetVisibleItems"/> call (the panel
    /// always calls that immediately before rendering its cards).
    /// </summary>
    public string? GetChannelCountry(string channelId)
    {
        var channels = _channelsSnapshot ??= _store.GetSubscriptionsCache();
        return channels.TryGetValue(channelId, out var entry) ? entry.Country : null;
    }

    public void MarkWatched(string videoId) => _store.MarkVideoWatched(videoId);
}
