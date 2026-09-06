using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Sync;

public class BackgroundSyncService
{
    private const int VideosPerChannel = 15;

    private readonly IYouTubeApiClient _api;
    private readonly SubscriptionStore _store;
    private readonly Func<Task<string?>> _getAccessToken;
    private Timer? _timer;

    public BackgroundSyncService(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)
    {
        _api = api;
        _store = store;
        _getAccessToken = getAccessToken;
    }

    /// <summary>
    /// Raised on the timer's threadpool thread after a sync run has written
    /// fresh data to the store. Subscribers that touch WPF UI MUST marshal to
    /// the UI thread themselves (e.g. via <c>Dispatcher.Invoke</c>).
    /// </summary>
    public event Action? SyncCompleted;

    public void Start(TimeSpan interval)
    {
        // An unhandled exception on a threadpool thread is fatal to the whole
        // process in modern .NET, and every routine failure mode here (expired
        // token, quota exhaustion, network drop) surfaces as an exception — so
        // the callback body must never let one escape.
        _timer = new Timer(_ =>
        {
            try
            {
                RunOnceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.LogError("Background sync failed", ex);
                Diagnostics.NotificationCenter.Report(DescribeSyncFailure(ex));
            }
        }, null, TimeSpan.Zero, interval);
    }

    public void Stop() => _timer?.Dispose();

    private static string DescribeSyncFailure(Exception ex)
    {
        if (ex is YouTubeApiException { StatusCode: System.Net.HttpStatusCode.Forbidden } e)
        {
            var body = e.ResponseBody ?? string.Empty;
            if (body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
                || body.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase))
                return "Background sync skipped — YouTube API daily quota is used up. The feed is showing cached videos.";
        }
        if (ex is YouTubeApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            return "Background sync couldn't run — sign in again.";
        return "Background sync failed — see the log file for details.";
    }

    public async Task RunOnceAsync()
    {
        var token = await _getAccessToken();
        if (token == null) return;

        var subscriptions = await _api.FetchAllSubscriptionsAsync(token);
        var channelIds = subscriptions.Select(s => s.ChannelId).ToList();
        var channelDetails = await _api.FetchChannelsDetailsAsync(token, channelIds);
        var previousCache = _store.GetSubscriptionsCache();
        var previousVideos = _store.GetVideosCache();

        var subscriptionsCache = new Dictionary<string, SubscriptionCacheEntry>();
        var videosCache = new Dictionary<string, List<VideoInfo>>();

        foreach (var sub in subscriptions)
        {
            channelDetails.TryGetValue(sub.ChannelId, out var details);
            previousCache.TryGetValue(sub.ChannelId, out var prevEntry);
            var previousEtag = prevEntry?.PlaylistEtag;

            if (details == null)
            {
                subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                    sub.Title, sub.Thumbnail, null, null, Dead: true, previousEtag, sub.SubscriptionId,
                    prevEntry?.UploadsNextPageToken, prevEntry?.HistoryComplete ?? false);
                continue;
            }

            var playlistResult = await _api.FetchRecentUploadIdsAsync(
                token, details.UploadsPlaylistId, previousEtag, VideosPerChannel);

            previousVideos.TryGetValue(sub.ChannelId, out var existingVideos);

            // Preserve the deep-history cursor across syncs: FeedExpansionService
            // advances it as the user scrolls, and a sync must not rewind that.
            // Only seed it (from this first page) when we've never had one.
            var nextPageToken = prevEntry?.UploadsNextPageToken;
            var historyComplete = prevEntry?.HistoryComplete ?? false;
            if (nextPageToken == null && !historyComplete)
            {
                nextPageToken = playlistResult.NotModified ? null : playlistResult.NextPageToken;
                historyComplete = !playlistResult.NotModified && playlistResult.NextPageToken == null;
            }

            subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                sub.Title, sub.Thumbnail, details.Country, details.UploadsPlaylistId, Dead: false,
                playlistResult.NotModified ? prevEntry?.PlaylistEtag : playlistResult.Etag,
                sub.SubscriptionId, nextPageToken, historyComplete);

            if (playlistResult.NotModified)
            {
                if (existingVideos != null) videosCache[sub.ChannelId] = existingVideos;
                continue;
            }

            if (playlistResult.VideoIds.Count == 0)
            {
                if (existingVideos != null) videosCache[sub.ChannelId] = existingVideos;
                continue;
            }

            // Merge the refreshed newest-N over whatever we already cached, so
            // videos pulled by FeedExpansionService (older than page 1) survive.
            var freshVideos = await _api.FetchVideosDetailsAsync(token, playlistResult.VideoIds);
            videosCache[sub.ChannelId] = VideoMerge.Dedup(existingVideos, freshVideos);
        }

        _store.SaveSubscriptionsCache(subscriptionsCache);
        _store.SaveVideosCache(videosCache);
        _store.SetLastSyncedAt(DateTimeOffset.UtcNow);

        SyncCompleted?.Invoke();
    }
}
