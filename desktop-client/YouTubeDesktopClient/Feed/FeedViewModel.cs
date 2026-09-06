// Feed/FeedViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;
using YouTubeDesktopClient.Sync;

namespace YouTubeDesktopClient.Feed;

public record FeedItem(VideoInfo Video, string Type, int DurationSeconds, bool IsWatched);

public class FeedViewModel : INotifyPropertyChanged
{
    // How many cards to reveal per scroll-triggered batch. The full sorted list
    // lives in memory; this only paces how fast it's handed to the virtualized
    // ItemsControl.
    private const int PageSize = 60;

    private readonly SubscriptionStore _store;
    private readonly IFeedExpansionService _expansion;
    private readonly Func<bool> _isAutoExpandEnabled;

    public string TypeFilter { get; set; } = "all";
    public string SortBy { get; set; } = "date";
    public bool HideWatched { get; set; }
    public string? ActiveGroupId { get; private set; }

    // Snapshot of the subscriptions cache taken once per GetVisibleItems call,
    // so per-card country lookups don't re-read and re-deserialize cache.json
    // once for every video on screen.
    private Dictionary<string, SubscriptionCacheEntry>? _channelsSnapshot;

    private List<FeedItem> _all = new();
    private int _rendered;
    private bool _isExpanding;

    public FeedViewModel(SubscriptionStore store,
        IFeedExpansionService? expansion = null,
        Func<bool>? isAutoExpandEnabled = null)
    {
        _store = store;
        _expansion = expansion ?? new NullFeedExpansionService();
        _isAutoExpandEnabled = isAutoExpandEnabled ?? (() => true);
    }

    /// <summary>The cards currently on screen. Bound as the feed's ItemsSource.</summary>
    public ObservableCollection<VideoCardViewModel> Items { get; } = new();

    /// <summary>True while a network history fetch is running — drives the footer spinner.</summary>
    public bool IsExpanding
    {
        get => _isExpanding;
        private set { if (_isExpanding == value) return; _isExpanding = value; OnPropertyChanged(); }
    }

    public bool QuotaExhausted => _expansion.QuotaExhausted;

    private bool HasMoreLocal => _rendered < _all.Count;

    public void SetActiveGroup(string? groupId)
    {
        ActiveGroupId = groupId;
        Reload();
    }

    /// <summary>
    /// Rebuilds the full sorted list from the store and re-fills the visible
    /// window with the first page. Called on construction, filter/group changes,
    /// and after a background sync.
    /// </summary>
    public void Reload()
    {
        _all = GetVisibleItems(ActiveGroupId);
        _rendered = 0;
        Items.Clear();
        AppendPage();
    }

    /// <summary>
    /// Scroll handler entry point. Reveals the next local page; when the local
    /// list is exhausted, asks the expansion service for more history (if the
    /// user hasn't turned that off and the quota isn't spent), then reveals what
    /// it fetched.
    /// </summary>
    public async Task LoadMoreAsync()
    {
        if (HasMoreLocal)
        {
            AppendPage();
            return;
        }

        if (IsExpanding || !_isAutoExpandEnabled() || _expansion.QuotaExhausted) return;

        var channelIds = ActiveChannelIds();
        if (channelIds.Count == 0) return;

        IsExpanding = true;
        try
        {
            var added = await _expansion.ExpandAsync(channelIds);
            if (!added) return;

            // Expansion only appends videos OLDER than everything already shown,
            // so the current Items prefix still matches _all[0.._rendered] —
            // recompute the tail and reveal the next page.
            _all = GetVisibleItems(ActiveGroupId);
            AppendPage();
        }
        finally
        {
            IsExpanding = false;
        }
    }

    private void AppendPage()
    {
        var next = _all.Skip(_rendered).Take(PageSize).ToList();
        foreach (var item in next)
        {
            Items.Add(new VideoCardViewModel(
                item,
                GetChannelCountry(item.Video.ChannelId),
                onPlay: PlayRequested,
                onToggleWatched: ToggleWatched,
                onAddToPlaylist: AddToPlaylistRequested));
        }
        _rendered += next.Count;
    }

    private IReadOnlyCollection<string> ActiveChannelIds()
    {
        if (ActiveGroupId == null) return _store.GetVideosCache().Keys.ToList();
        return _store.GetGroups().TryGetValue(ActiveGroupId, out var group)
            ? group.ChannelIds
            : Array.Empty<string>();
    }

    /// <summary>Raised when a card's Play button is clicked (videoId, title); the
    /// panel opens the video tab and titles it.</summary>
    public event Action<string, string>? PlayRequestedEvent;
    private void PlayRequested(string videoId, string title) => PlayRequestedEvent?.Invoke(videoId, title);

    /// <summary>Raised when a card's Save button is clicked (videoId, title); the
    /// shell opens the "add to playlist" picker.</summary>
    public event Action<string, string>? AddToPlaylistRequestedEvent;
    private void AddToPlaylistRequested(string videoId, string title) =>
        AddToPlaylistRequestedEvent?.Invoke(videoId, title);

    private void ToggleWatched(string videoId)
    {
        MarkWatched(videoId);
        if (HideWatched)
        {
            Reload();
            return;
        }
        var card = Items.FirstOrDefault(c => c.VideoId == videoId);
        if (card != null) card.IsWatched = true;
    }

    public List<FeedItem> GetVisibleItems(string? activeGroupId)
    {
        var videosCache = _store.GetVideosCache();
        var watchedIds = _store.GetWatchedVideoIds();
        _channelsSnapshot = _store.GetSubscriptionsCache();

        IEnumerable<string> channelIds = activeGroupId == null
            ? videosCache.Keys
            : _store.GetGroups().TryGetValue(activeGroupId, out var group) ? group.ChannelIds : Enumerable.Empty<string>();

        var items = channelIds
            .Where(videosCache.ContainsKey)
            .SelectMany(channelId => videosCache[channelId])
            .Select(video =>
            {
                var durationSeconds = VideoClassifier.ParseIsoDuration(video.Duration);
                var type = VideoClassifier.ClassifyVideoType(video.LiveBroadcastContent, durationSeconds);
                return new FeedItem(video, type, durationSeconds, watchedIds.Contains(video.VideoId));
            });

        if (TypeFilter != "all")
            items = items.Where(i => i.Type == TypeFilter);
        if (HideWatched)
            items = items.Where(i => !i.IsWatched);

        items = SortBy switch
        {
            "duration" => items.OrderByDescending(i => i.DurationSeconds),
            "views" => items.OrderByDescending(i => i.Video.ViewCount),
            _ => items.OrderByDescending(i => i.Video.PublishedAt),
        };

        return items.ToList();
    }

    /// <summary>
    /// Country lives on the channel's cache entry rather than on the video, so
    /// the feed card has to look it up by channel id. Served from the snapshot
    /// taken by the most recent <see cref="GetVisibleItems"/> call.
    /// </summary>
    public string? GetChannelCountry(string channelId)
    {
        var channels = _channelsSnapshot ??= _store.GetSubscriptionsCache();
        return channels.TryGetValue(channelId, out var entry) ? entry.Country : null;
    }

    public void MarkWatched(string videoId) => _store.MarkVideoWatched(videoId);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
