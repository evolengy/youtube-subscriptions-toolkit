// Views/FeedPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Views;

public partial class FeedPanel : UserControl
{
    private readonly FeedViewModel _viewModel;
    private readonly Action<string> _onVideoClicked;
    private string? _activeGroupId;

    public FeedPanel(FeedViewModel viewModel, Action<string> onVideoClicked)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onVideoClicked = onVideoClicked;
        RefreshFeed();
    }

    public void SetActiveGroup(string? groupId)
    {
        _activeGroupId = groupId;
        RefreshFeed();
    }

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        _viewModel.TypeFilter = ((ComboBoxItem)TypeFilterBox.SelectedItem).Content.ToString()!;
        _viewModel.SortBy = ((ComboBoxItem)SortByBox.SelectedItem).Content.ToString()!;
        _viewModel.HideWatched = HideWatchedBox.IsChecked == true;
        RefreshFeed();
    }

    private void RefreshFeed()
    {
        VideoGrid.Items.Clear();
        foreach (var item in _viewModel.GetVisibleItems(_activeGroupId))
        {
            var card = new StackPanel { Width = 220, Margin = new Thickness(4) };
            card.Children.Add(new TextBlock { Text = item.Video.Title, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new TextBlock { Text = $"{item.Type} · {item.Video.ViewCount} views" });
            var watchBtn = new Button { Content = item.IsWatched ? "Watched" : "Mark watched" };
            watchBtn.Click += (_, _) => { _viewModel.MarkWatched(item.Video.VideoId); RefreshFeed(); };
            card.Children.Add(watchBtn);
            var playBtn = new Button { Content = "Play" };
            playBtn.Click += (_, _) => _onVideoClicked(item.Video.VideoId);
            card.Children.Add(playBtn);
            VideoGrid.Items.Add(card);
        }
    }
}
