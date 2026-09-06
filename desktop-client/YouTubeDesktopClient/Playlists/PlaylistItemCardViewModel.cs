// Playlists/PlaylistItemCardViewModel.cs
using System.Windows.Input;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Playlists;

/// <summary>One card in a playlist's video grid. A deleted / private entry keeps
/// its row (so "Remove" still works) but has no playable video id.</summary>
public sealed class PlaylistItemCardViewModel
{
    public PlaylistItemCardViewModel(PlaylistItemEntry entry,
        Action<string, string> onPlay, Action<string, string> onRemove)
    {
        VideoId = entry.VideoId;
        PlaylistItemId = entry.PlaylistItemId;
        Title = string.IsNullOrWhiteSpace(entry.Title) ? "(unavailable video)" : entry.Title;
        ThumbnailUrl = entry.ThumbnailUrl;
        MetaLine = string.IsNullOrWhiteSpace(entry.ChannelTitle) ? "" : entry.ChannelTitle;

        PlayCommand = new RelayCommand(() => { if (VideoId.Length > 0) onPlay(VideoId, Title); });
        RemoveCommand = new RelayCommand(() => onRemove(PlaylistItemId, Title));
    }

    public string VideoId { get; }
    public string PlaylistItemId { get; }
    public string Title { get; }
    public string? ThumbnailUrl { get; }
    public string MetaLine { get; }

    public ICommand PlayCommand { get; }
    public ICommand RemoveCommand { get; }
}
