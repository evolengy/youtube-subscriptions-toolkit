// Sync/FeedExpansionService.cs
using System.Net;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Sync;

public class FeedExpansionService : IFeedExpansionService
{
    // Uploads pages are 50 items; playlistItems.list + videos.list each cost 1
    // quota unit, so one Expand across N channels is ~2N units.
    private const int PageSize = 50;
    // Stop deepening a single channel past this many cached videos — keeps one
    // cache.json from ballooning for channels with thousands of uploads.
    private const int MaxCachedPerChannel = 500;
    private const int MaxParallelism = 4;

    private readonly IYouTubeApiClient _api;
    private readonly SubscriptionStore _store;
    private readonly Func<Task<string?>> _getAccessToken;

    public FeedExpansionService(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)
    {
        _api = api;
        _store = store;
        _getAccessToken = getAccessToken;
    }

    public bool QuotaExhausted { get; private set; }

    public async Task<bool> ExpandAsync(IReadOnlyCollection<string> channelIds, CancellationToken ct = default)
    {
        if (QuotaExhausted) return false;

        var token = await _getAccessToken();
        if (token == null) return false;

        var subs = _store.GetSubscriptionsCache();
        var videos = _store.GetVideosCache();

        var candidates = channelIds
            .Where(id => subs.TryGetValue(id, out var e)
                         && e is { Dead: false, HistoryComplete: false }
                         && !string.IsNullOrEmpty(e.UploadsPlaylistId)
                         && !string.IsNullOrEmpty(e.UploadsNextPageToken)
                         && (!videos.TryGetValue(id, out var vs) || vs.Count < MaxCachedPerChannel))
            .ToList();

        if (candidates.Count == 0) return false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(MaxParallelism);
        var addedAny = 0;

        var tasks = candidates.Select(async channelId =>
        {
            await gate.WaitAsync(cts.Token);
            try
            {
                if (await ExpandChannelAsync(token, channelId, subs[channelId], cts.Token))
                    Interlocked.Exchange(ref addedAny, 1);
            }
            catch (OperationCanceledException) { }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }

        return addedAny == 1;
    }

    private async Task<bool> ExpandChannelAsync(
        string token, string channelId, SubscriptionCacheEntry entry, CancellationToken ct)
    {
        PlaylistItemsResult page;
        try
        {
            page = await _api.FetchRecentUploadIdsAsync(
                token, entry.UploadsPlaylistId!, previousEtag: null,
                maxResults: PageSize, pageToken: entry.UploadsNextPageToken);
        }
        catch (YouTubeApiException ex) when (IsQuotaError(ex))
        {
            QuotaExhausted = true;
            Logger.LogError("Feed expansion stopped: YouTube API quota exhausted", ex);
            throw new OperationCanceledException(ct);
        }
        catch (YouTubeApiException ex)
        {
            Logger.LogError($"Feed expansion failed for channel {channelId}; skipping", ex);
            return false;
        }

        var historyComplete = string.IsNullOrEmpty(page.NextPageToken);

        if (page.VideoIds.Count == 0)
        {
            _store.AppendChannelHistory(channelId, Array.Empty<VideoInfo>(), page.NextPageToken, historyComplete);
            return false;
        }

        List<VideoInfo> details;
        try
        {
            details = await _api.FetchVideosDetailsAsync(token, page.VideoIds);
        }
        catch (YouTubeApiException ex) when (IsQuotaError(ex))
        {
            QuotaExhausted = true;
            Logger.LogError("Feed expansion stopped: YouTube API quota exhausted", ex);
            throw new OperationCanceledException(ct);
        }

        _store.AppendChannelHistory(channelId, details, page.NextPageToken, historyComplete);
        return details.Count > 0;
    }

    /// <summary>
    /// A 403 whose body names a quota/rate reason is terminal for the day — stop
    /// auto-expanding. A plain 403 (permissions, disabled API) is not: it's
    /// channel-specific and expansion should just skip that channel.
    /// </summary>
    private static bool IsQuotaError(YouTubeApiException ex)
    {
        if (ex.StatusCode != HttpStatusCode.Forbidden) return false;
        var body = ex.ResponseBody ?? string.Empty;
        return body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
            || body.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || body.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase);
    }
}
