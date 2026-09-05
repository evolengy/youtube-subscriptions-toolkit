namespace YouTubeDesktopClient.Api;

public record SubscriptionEntry(string SubscriptionId, string ChannelId, string Title, string? Thumbnail);

public record ChannelDetails(string ChannelId, string? Country, string UploadsPlaylistId);

public record PlaylistItemsResult(List<string> VideoIds, string? Etag, bool NotModified);
