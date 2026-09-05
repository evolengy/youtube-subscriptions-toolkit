using YouTubeDesktopClient.Api;
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

    public void Start(TimeSpan interval)
    {
        _timer = new Timer(_ => RunOnceAsync().GetAwaiter().GetResult(), null, TimeSpan.Zero, interval);
    }

    public void Stop() => _timer?.Dispose();

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
            var previousEtag = previousCache.TryGetValue(sub.ChannelId, out var prevEntry) ? prevEntry.PlaylistEtag : null;

            if (details == null)
            {
                subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                    sub.Title, sub.Thumbnail, null, null, Dead: true, previousEtag, sub.SubscriptionId);
                continue;
            }

            var playlistResult = await _api.FetchRecentUploadIdsAsync(
                token, details.UploadsPlaylistId, previousEtag, VideosPerChannel);

            subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                sub.Title, sub.Thumbnail, details.Country, details.UploadsPlaylistId, Dead: false, playlistResult.Etag, sub.SubscriptionId);

            if (playlistResult.NotModified)
            {
                if (previousVideos.TryGetValue(sub.ChannelId, out var existing))
                    videosCache[sub.ChannelId] = existing;
                continue;
            }

            if (playlistResult.VideoIds.Count == 0) continue;
            videosCache[sub.ChannelId] = await _api.FetchVideosDetailsAsync(token, playlistResult.VideoIds);
        }

        _store.SaveSubscriptionsCache(subscriptionsCache);
        _store.SaveVideosCache(videosCache);
        _store.SetLastSyncedAt(DateTimeOffset.UtcNow);
    }
}
