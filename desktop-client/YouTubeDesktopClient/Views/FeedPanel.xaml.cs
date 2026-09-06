// Views/FeedPanel.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

        Cards.NearEndReached += OnNearEndReached;

        _viewModel.Reload();
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

    private void ApplyDensity() => Cards.SetDensity(_settings.Density);

    private void Filters_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.TypeFilter = ((ComboBoxItem)TypeFilterBox.SelectedItem).Content.ToString()!;
        _viewModel.SortBy = ((ComboBoxItem)SortByBox.SelectedItem).Content.ToString()!;
        _viewModel.HideWatched = HideWatchedBox.IsChecked == true;
        _viewModel.Reload();
    }

    private async void OnNearEndReached()
    {
        if (_loadingMore) return;
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
