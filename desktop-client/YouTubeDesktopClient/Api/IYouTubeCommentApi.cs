namespace YouTubeDesktopClient.Api;

/// <summary>
/// Reading + writing video comments. Reads (`*.list`) are 1 unit; posting a
/// comment or reply is 50. <see cref="YouTubeApiClient"/> implements every I*Api
/// interface. <see cref="PostCommentAsync"/> is also on <see cref="IYouTubeApiClient"/>
/// (the player action bar predates this split).
/// </summary>
public interface IYouTubeCommentApi
{
    Task<CommentPage> ListCommentThreadsAsync(string accessToken, string videoId, string? pageToken = null);
    Task<ReplyPage> ListRepliesAsync(string accessToken, string parentId, string? pageToken = null);
    Task ReplyToCommentAsync(string accessToken, string parentId, string text);
    Task PostCommentAsync(string accessToken, string videoId, string text);
}
