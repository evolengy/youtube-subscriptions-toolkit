namespace YouTubeDesktopClient.Api;

public interface IYouTubeApiClient
{
    Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken);
    Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds);
    Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15);
    Task<List<Storage.Models.VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds);
    Task UnsubscribeAsync(string accessToken, string subscriptionId);
}
