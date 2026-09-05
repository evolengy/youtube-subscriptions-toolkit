// Views/FeedPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Views;

public partial class FeedPanel : UserControl
{
    private readonly FeedViewModel _viewModel;
    private readonly Action<string> _onVideoClicked;
    private string? _activeGroupId;

    public FeedPanel(FeedViewModel viewModel, Action<string> onVideoClicked)
    {
        _viewModel = viewModel;
        _onVideoClicked = onVideoClicked;
        InitializeComponent();

        // Wired here rather than in XAML: the ComboBoxItems' IsSelected="True"
        // fires SelectionChanged synchronously while InitializeComponent() is
        // still parsing the tree, before sibling controls (e.g. SortByBox
        // while TypeFilterBox is being built) exist yet. Attaching after
        // InitializeComponent() guarantees every named control is live first.
        TypeFilterBox.SelectionChanged += Filters_Changed;
        SortByBox.SelectionChanged += Filters_Changed;
        HideWatchedBox.Checked += Filters_Changed;
        HideWatchedBox.Unchecked += Filters_Changed;

        Refresh();
    }

    public void SetActiveGroup(string? groupId)
    {
        _activeGroupId = groupId;
        Refresh();
    }

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        _viewModel.TypeFilter = ((ComboBoxItem)TypeFilterBox.SelectedItem).Content.ToString()!;
        _viewModel.SortBy = ((ComboBoxItem)SortByBox.SelectedItem).Content.ToString()!;
        _viewModel.HideWatched = HideWatchedBox.IsChecked == true;
        Refresh();
    }

    /// <summary>
    /// Re-renders the cards from the store. Public so a completed background
    /// sync can push fresh data onto the screen (see App's SyncCompleted
    /// handler) rather than leaving the feed stale until a filter is toggled.
    /// Must be called on the UI thread.
    /// </summary>
    public void Refresh()
    {
        VideoGrid.Items.Clear();
        foreach (var item in _viewModel.GetVisibleItems(_activeGroupId))
        {
            var card = new StackPanel { Width = 220, Margin = new Thickness(4) };

            var thumbnail = TryLoadThumbnail(item.Video.Thumbnail);
            if (thumbnail != null)
                card.Children.Add(new Image { Source = thumbnail, Width = 212, Stretch = Stretch.Uniform });

            card.Children.Add(new TextBlock { Text = item.Video.Title, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new TextBlock { Text = BuildMetaLine(item) });

            var watchBtn = new Button { Content = item.IsWatched ? "Watched" : "Mark watched" };
            watchBtn.Click += (_, _) => { _viewModel.MarkWatched(item.Video.VideoId); Refresh(); };
            card.Children.Add(watchBtn);
            var playBtn = new Button { Content = "Play" };
            playBtn.Click += (_, _) => _onVideoClicked(item.Video.VideoId);
            card.Children.Add(playBtn);
            VideoGrid.Items.Add(card);
        }
    }

    private string BuildMetaLine(FeedItem item)
    {
        var parts = new List<string>
        {
            item.Type,
            FormatDuration(item.DurationSeconds),
            $"{item.Video.ViewCount:N0} views",
        };
        var country = _viewModel.GetChannelCountry(item.Video.ChannelId);
        if (!string.IsNullOrWhiteSpace(country)) parts.Add(country);
        return string.Join(" · ", parts);
    }

    private static string FormatDuration(int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static BitmapImage? TryLoadThumbnail(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            // A remote BitmapImage downloads asynchronously and reports its own
            // failures, so only a malformed URL can throw here — but a bad URL
            // in the cache must not take out the whole feed render.
            return new BitmapImage(new Uri(url));
        }
        catch (Exception ex) when (ex is UriFormatException or NotSupportedException)
        {
            Logger.LogError($"Skipping unusable thumbnail URL '{url}'", ex);
            return null;
        }
    }
}
