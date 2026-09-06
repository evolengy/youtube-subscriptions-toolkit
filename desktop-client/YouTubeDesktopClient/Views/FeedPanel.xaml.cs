// Views/FeedPanel.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Settings;

namespace YouTubeDesktopClient.Views;

public partial class FeedPanel : UserControl
{
    private readonly FeedViewModel _viewModel;
    private readonly AppSettingsViewModel _settings;
    private bool _loadingMore;

    public FeedPanel(FeedViewModel viewModel, Action<string, string> onVideoClicked, AppSettingsViewModel settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PlayRequestedEvent += onVideoClicked;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _settings.DensityChanged += ApplyDensity;
        ApplyDensity();

        // Wired here rather than in XAML: the ComboBoxItems' IsSelected="True"
        // fires SelectionChanged synchronously while InitializeComponent() is
        // still parsing the tree, before sibling controls exist yet.
        TypeFilterBox.SelectionChanged += Filters_Changed;
        SortByBox.SelectionChanged += Filters_Changed;
        HideWatchedBox.Checked += Filters_Changed;
        HideWatchedBox.Unchecked += Filters_Changed;

        // The VirtualizingWrapPanel reports a sub-pixel horizontal extent at some
        // widths, and the inner ScrollViewer then shows a phantom horizontal bar
        // that scrolls nothing. Pinning the real ScrollViewer to Disabled is the
        // reliable fix (the XAML attached property alone doesn't stick against the
        // panel's IScrollInfo) — and it has to be re-applied after relayout.
        VideoGrid.Loaded += (_, _) => PinNoHorizontalScroll();
        VideoGrid.SizeChanged += (_, _) => PinNoHorizontalScroll();

        _viewModel.Reload();
    }

    private ScrollViewer? _gridScrollViewer;

    private void PinNoHorizontalScroll()
    {
        _gridScrollViewer ??= FindDescendant<ScrollViewer>(VideoGrid);
        if (_gridScrollViewer is { } sv && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    public void SetActiveGroup(string? groupId) => _viewModel.SetActiveGroup(groupId);

    /// <summary>
    /// Re-renders from the store. Public so a completed background sync can push
    /// fresh data onto the screen (see App's SyncCompleted handler). Must be
    /// called on the UI thread.
    /// </summary>
    public void Refresh()
    {
        _viewModel.Reload();
        UpdateStatus();
    }

    private void ApplyDensity()
    {
        var layout = FeedLayout.For(_settings.Density);
        Resources["Feed.CardWidth"] = layout.CardWidth;
        Resources["Feed.ThumbHeight"] = layout.ThumbnailHeight;
        Resources["Feed.TitleHeight"] = layout.TitleHeight;
    }

    private void Filters_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.TypeFilter = ((ComboBoxItem)TypeFilterBox.SelectedItem).Content.ToString()!;
        _viewModel.SortBy = ((ComboBoxItem)SortByBox.SelectedItem).Content.ToString()!;
        _viewModel.HideWatched = HideWatchedBox.IsChecked == true;
        _viewModel.Reload();
    }

    private async void VideoGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && e.VerticalChange == 0) return; // layout pass, not a user scroll
        if (_loadingMore) return;

        // Within two viewports of the bottom -> pull the next batch.
        var distanceToEnd = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
        if (distanceToEnd > e.ViewportHeight * 2) return;

        _loadingMore = true;
        try
        {
            await _viewModel.LoadMoreAsync();
        }
        finally
        {
            _loadingMore = false;
            UpdateStatus();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FeedViewModel.IsExpanding)) UpdateStatus();
    }

    private bool _quotaReported;

    private void UpdateStatus()
    {
        // A live "loading more" line stays inline; the quota problem is a
        // notification, not a permanent banner glued to the feed.
        if (_viewModel.QuotaExhausted && !_quotaReported)
        {
            _quotaReported = true;
            NotificationCenter.Report(
                "YouTube API daily quota is used up — older videos will load again after it resets.");
        }
        if (!_viewModel.QuotaExhausted) _quotaReported = false;

        var loading = _viewModel.IsExpanding;
        StatusText.Text = loading ? "Loading more from YouTube…" : string.Empty;
        StatusText.Visibility = loading
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }
}
