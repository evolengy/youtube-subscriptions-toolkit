namespace YouTubeDesktopClient.Api;

public record SubscriptionEntry(string SubscriptionId, string ChannelId, string Title, string? Thumbnail);

/// <summary>The signed-in user's own channel — the stable key for per-account
/// storage and what the toolbar account control shows.</summary>
public record MyChannel(string ChannelId, string Title, string? ThumbnailUrl, string? Handle);

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

/// <summary>One of the signed-in user's playlists (the sidebar list).</summary>
public record PlaylistSummary(
    string Id, string Title, string Description, long ItemCount, string? ThumbnailUrl, string Privacy);

/// <summary>One video inside a playlist. <see cref="PlaylistItemId"/> is the
/// membership row's id — the handle needed to remove it (not the video id).</summary>
public record PlaylistItemEntry(
    string PlaylistItemId, string VideoId, string Title, string? ThumbnailUrl,
    string ChannelTitle, int Position);

// ---- comments ----

public record CommentInfo(
    string Id, string Author, string? AuthorAvatarUrl, string Text, long LikeCount, DateTimeOffset PublishedAt);

/// <summary>A top-level comment plus a preview of its replies (the API returns up
/// to ~5 inline). <see cref="TotalReplyCount"/> drives the "view N replies" toggle.</summary>
public record CommentThread(
    string Id, CommentInfo Top, int TotalReplyCount, IReadOnlyList<CommentInfo> PreviewReplies);

public record CommentPage(IReadOnlyList<CommentThread> Threads, string? NextPageToken);

public record ReplyPage(IReadOnlyList<CommentInfo> Replies, string? NextPageToken);
