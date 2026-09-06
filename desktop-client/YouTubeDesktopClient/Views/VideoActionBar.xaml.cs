// Views/VideoActionBar.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YouTubeDesktopClient.Player;

namespace YouTubeDesktopClient.Views;

/// <summary>
/// The Like / Dislike / Subscribe / Comment strip above the player. Pure view:
/// every decision lives in <see cref="VideoActionsViewModel"/>; this just reflects
/// its state and forwards clicks. Handlers are async void because they are UI
/// event handlers — the view model swallows and reports failures, so nothing
/// escapes here.
/// </summary>
public partial class VideoActionBar : UserControl
{
    private readonly VideoActionsViewModel _vm;

    public VideoActionBar(VideoActionsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        _vm.PropertyChanged += OnViewModelChanged;
        Refresh();
    }

    /// <summary>Point the bar at a newly shown video and (re)load its state.</summary>
    public async void ShowVideo(string videoId)
    {
        CommentBox.Clear();
        await _vm.LoadAsync(videoId);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        ChannelText.Text = _vm.ChannelTitle;

        var live = _vm.CanInteract && !_vm.Busy;
        LikeButton.IsEnabled = DislikeButton.IsEnabled = SubscribeButton.IsEnabled = live;
        CommentButton.IsEnabled = CommentBox.IsEnabled = live;

        LikeButton.Foreground = _vm.IsLiked ? Brush("Brush.Accent") : Brush("Brush.TextSecondary");
        DislikeButton.Foreground = _vm.IsDisliked ? Brush("Brush.Accent") : Brush("Brush.TextSecondary");

        SubscribeButton.Content = _vm.SubscribeLabel;
        // Accent fill when not yet subscribed; plain button once subscribed.
        SubscribeButton.Style = _vm.IsSubscribed
            ? (Style)Application.Current.FindResource(typeof(Button))
            : (Style)FindResource("Button.Primary");

        StatusText.Text = _vm.Status ?? string.Empty;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private async void Like_Click(object sender, RoutedEventArgs e) => await _vm.ToggleRatingAsync("like");

    private async void Dislike_Click(object sender, RoutedEventArgs e) => await _vm.ToggleRatingAsync("dislike");

    private async void Subscribe_Click(object sender, RoutedEventArgs e) => await _vm.ToggleSubscribeAsync();

    private async void Comment_Click(object sender, RoutedEventArgs e)
    {
        var text = CommentBox.Text;
        await _vm.PostCommentAsync(text);
        if (_vm.Status == "Comment posted.") CommentBox.Clear();
    }
}
