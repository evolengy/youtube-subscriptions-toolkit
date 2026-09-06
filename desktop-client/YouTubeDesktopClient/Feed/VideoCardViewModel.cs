// Feed/VideoCardViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace YouTubeDesktopClient.Feed;

/// <summary>
/// One feed card. Immutable except for <see cref="IsWatched"/>, which flips in
/// place when the user marks a video watched so the card updates without the
/// whole feed re-rendering.
/// </summary>
public sealed class VideoCardViewModel : INotifyPropertyChanged
{
    private bool _isWatched;

    public VideoCardViewModel(FeedItem item, string? country,
        Action<string, string> onPlay, Action<string> onToggleWatched,
        Action<string, string>? onAddToPlaylist = null)
    {
        VideoId = item.Video.VideoId;
        Title = item.Video.Title;
        ThumbnailUrl = item.Video.Thumbnail;
        MetaLine = FeedFormatting.MetaLine(item, country);
        _isWatched = item.IsWatched;

        PlayCommand = new RelayCommand(() => onPlay(VideoId, Title));
        ToggleWatchedCommand = new RelayCommand(() => onToggleWatched(VideoId));
        AddToPlaylistCommand = new RelayCommand(() => onAddToPlaylist?.Invoke(VideoId, Title));
    }

    public string VideoId { get; }
    public string Title { get; }
    public string? ThumbnailUrl { get; }
    public string MetaLine { get; }

    public bool IsWatched
    {
        get => _isWatched;
        set
        {
            if (_isWatched == value) return;
            _isWatched = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WatchButtonLabel));
        }
    }

    public string WatchButtonLabel => IsWatched ? "Watched" : "Mark watched";

    public ICommand PlayCommand { get; }
    public ICommand ToggleWatchedCommand { get; }
    public ICommand AddToPlaylistCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
