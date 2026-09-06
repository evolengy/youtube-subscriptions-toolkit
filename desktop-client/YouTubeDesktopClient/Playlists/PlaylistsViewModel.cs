// Playlists/PlaylistsViewModel.cs
using System.Collections.ObjectModel;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Playlists;

/// <summary>
/// The signed-in user's playlists — the shared source for both the Playlists
/// panel and the "Save to playlist" picker. The list is fetched once per session
/// (<see cref="EnsureLoadedAsync"/>) and mutated in place on create / rename /
/// delete / add so the UI updates without a refetch. Every failure lands in
/// <see cref="NotificationCenter"/>; nothing is added locally that the server
/// rejected.
/// </summary>
public sealed class PlaylistsViewModel
{
    private readonly IYouTubePlaylistApi _api;
    private readonly Func<Task<string?>> _getToken;
    private bool _loaded;

    public PlaylistsViewModel(IYouTubePlaylistApi api, Func<Task<string?>> getToken)
    {
        _api = api;
        _getToken = getToken;
    }

    public ObservableCollection<PlaylistSummary> Playlists { get; } = new();

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_loaded && !force) return;
        var token = await _getToken();
        if (token is null) return;
        try
        {
            var lists = await _api.ListMyPlaylistsAsync(token);
            Playlists.Clear();
            foreach (var p in lists) Playlists.Add(p);
            _loaded = true;
        }
        catch (Exception ex) { Report("Loading playlists", ex); }
    }

    public async Task<bool> CreateAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            var id = await _api.CreatePlaylistAsync(token, title.Trim(), description: null);
            Playlists.Insert(0, new PlaylistSummary(id, title.Trim(), "", 0, null, "private"));
            return true;
        }
        catch (Exception ex) { Report("Creating the playlist", ex); return false; }
    }

    public async Task<bool> RenameAsync(string playlistId, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return false;
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            var existing = Find(playlistId);
            await _api.UpdatePlaylistAsync(token, playlistId, newTitle.Trim(), existing?.Description);
            Replace(playlistId, p => p with { Title = newTitle.Trim() });
            return true;
        }
        catch (Exception ex) { Report("Renaming the playlist", ex); return false; }
    }

    public async Task<bool> DeleteAsync(string playlistId)
    {
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            await _api.DeletePlaylistAsync(token, playlistId);
            if (Find(playlistId) is { } p) Playlists.Remove(p);
            return true;
        }
        catch (Exception ex) { Report("Deleting the playlist", ex); return false; }
    }

    public async Task<IReadOnlyList<PlaylistItemEntry>> GetItemsAsync(string playlistId)
    {
        var token = await _getToken();
        if (token is null) return Array.Empty<PlaylistItemEntry>();
        try
        {
            return await _api.ListPlaylistItemsAsync(token, playlistId);
        }
        catch (Exception ex) { Report("Loading the playlist", ex); return Array.Empty<PlaylistItemEntry>(); }
    }

    public async Task<bool> AddVideoAsync(string playlistId, string videoId)
    {
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            await _api.AddToPlaylistAsync(token, playlistId, videoId);
            Replace(playlistId, p => p with { ItemCount = p.ItemCount + 1 });
            return true;
        }
        catch (Exception ex) { Report("Adding to the playlist", ex); return false; }
    }

    public async Task<bool> RemoveItemAsync(string playlistId, string playlistItemId)
    {
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            await _api.RemoveFromPlaylistAsync(token, playlistItemId);
            Replace(playlistId, p => p with { ItemCount = Math.Max(0, p.ItemCount - 1) });
            return true;
        }
        catch (Exception ex) { Report("Removing from the playlist", ex); return false; }
    }

    private PlaylistSummary? Find(string id)
    {
        foreach (var p in Playlists) if (p.Id == id) return p;
        return null;
    }

    private void Replace(string id, Func<PlaylistSummary, PlaylistSummary> update)
    {
        for (int i = 0; i < Playlists.Count; i++)
            if (Playlists[i].Id == id) { Playlists[i] = update(Playlists[i]); return; }
    }

    private static void Report(string what, Exception ex)
    {
        Logger.LogError($"{what} failed", ex);
        NotificationCenter.Report($"{what} failed — {ApiErrorText.Describe(ex)}");
    }
}
