namespace YouTubeDesktopClient.Api;

/// <summary>
/// Playlist CRUD + membership. Reads (`*.list`) cost 1 unit; every write
/// (`insert`/`update`/`delete`) costs 50 — callers should treat "add to playlist"
/// as a deliberate action, not something to fire in bulk.
/// <see cref="YouTubeApiClient"/> implements every I*Api interface.
/// </summary>
public interface IYouTubePlaylistApi
{
    Task<List<PlaylistSummary>> ListMyPlaylistsAsync(string accessToken);
    Task<string> CreatePlaylistAsync(string accessToken, string title, string? description, string privacy = "private");
    Task UpdatePlaylistAsync(string accessToken, string playlistId, string title, string? description);
    Task DeletePlaylistAsync(string accessToken, string playlistId);

    Task<List<PlaylistItemEntry>> ListPlaylistItemsAsync(string accessToken, string playlistId);
    /// <summary>Adds the video and returns the new playlistItem id.</summary>
    Task<string> AddToPlaylistAsync(string accessToken, string playlistId, string videoId);
    Task RemoveFromPlaylistAsync(string accessToken, string playlistItemId);
}
