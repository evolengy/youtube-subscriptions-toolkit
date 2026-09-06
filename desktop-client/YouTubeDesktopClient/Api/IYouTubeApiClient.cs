namespace YouTubeDesktopClient.Api;

public interface IYouTubeApiClient
{
    Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken);
    Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds);
    Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15, string? pageToken = null);
    Task<List<Storage.Models.VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds);
    Task UnsubscribeAsync(string accessToken, string subscriptionId);

    // ---- player action bar ----
    /// <summary>Owner channel + the user's current rating/subscription for a video.</summary>
    Task<VideoActionState> GetVideoActionStateAsync(string accessToken, string videoId);
    /// <summary>rating is "like", "dislike" or "none".</summary>
    Task RateVideoAsync(string accessToken, string videoId, string rating);
    /// <summary>Returns the new subscription id.</summary>
    Task<string> SubscribeAsync(string accessToken, string channelId);
    Task PostCommentAsync(string accessToken, string videoId, string text);
}
