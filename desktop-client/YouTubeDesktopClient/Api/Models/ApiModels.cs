namespace YouTubeDesktopClient.Api;

public record SubscriptionEntry(string SubscriptionId, string ChannelId, string Title, string? Thumbnail);

public record ChannelDetails(string ChannelId, string? Country, string UploadsPlaylistId);

/// <summary>The channel that published a video, plus the signed-in user's current
/// relationship to it, plus the bits of video metadata the native player page
/// shows — all from the one <c>videos?part=snippet,statistics</c> call the action
/// bar already makes, so the metadata costs no extra quota. The trailing fields
/// are defaulted: older callers / test fakes constructing this with four args
/// still compile.</summary>
public record VideoActionState(
    string ChannelId,
    string ChannelTitle,
    string Rating,              // "like" | "dislike" | "none"
    string? SubscriptionId,     // non-null when the user is already subscribed
    string Description = "",
    DateTimeOffset? PublishedAt = null,
    long ViewCount = 0);

public record PlaylistItemsResult(
    List<string> VideoIds,
    string? Etag,
    bool NotModified,
    // Cursor for the *next* (older) page of this uploads playlist. Null when the
    // response had no nextPageToken, i.e. the playlist has no more history.
    string? NextPageToken = null);
