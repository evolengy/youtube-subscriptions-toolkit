using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Channels;

public class ChannelManagementViewModel
{
    private readonly IYouTubeApiClient _api;
    private readonly SubscriptionStore _store;
    private readonly Func<Task<string?>> _getAccessToken;

    public ChannelManagementViewModel(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)
    {
        _api = api;
        _store = store;
        _getAccessToken = getAccessToken;
    }

    public List<(string ChannelId, SubscriptionCacheEntry Entry)> GetDeadChannels() =>
        _store.GetSubscriptionsCache()
            .Where(kv => kv.Value.Dead)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public async Task UnsubscribeAsync(string channelId, string subscriptionId)
    {
        var token = await _getAccessToken() ?? throw new InvalidOperationException("Sign in first.");
        await _api.UnsubscribeAsync(token, subscriptionId);

        var cache = _store.GetSubscriptionsCache();
        cache.Remove(channelId);
        _store.SaveSubscriptionsCache(cache);
    }
}
