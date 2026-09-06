// Player/CommentsView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Player;

/// <summary>
/// The comment list under the player (embed mode only). Built imperatively — the
/// per-thread expand / reply state is simpler to drive in code than in nested
/// DataTemplates. Backed by <see cref="CommentsViewModel"/>.
/// </summary>
public partial class CommentsView : UserControl
{
    private readonly CommentsViewModel _vm;

    public CommentsView(CommentsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.Changed += () => Dispatcher.Invoke(Rebuild);
    }

    public async void Show(string videoId)
    {
        ComposeBox.Clear();
        await _vm.LoadAsync(videoId);
    }

    private async void Compose_Click(object sender, RoutedEventArgs e)
    {
        var text = ComposeBox.Text;
        ComposeButton.IsEnabled = false;
        if (await _vm.PostAsync(text)) ComposeBox.Clear();
        ComposeButton.IsEnabled = true;
    }

    private void Rebuild()
    {
        ComposeRow.Visibility = _vm.CanComment ? Visibility.Visible : Visibility.Collapsed;
        ThreadList.Children.Clear();

        if (!_vm.CanComment)
        {
            ThreadList.Children.Add(Meta("Sign in to read and post comments."));
            return;
        }
        if (_vm.Threads.Count == 0)
        {
            ThreadList.Children.Add(Meta(_vm.LoadFailed
                ? "Couldn't load comments — see notifications."
                : "No comments yet."));
            return;
        }

        foreach (var thread in _vm.Threads)
            ThreadList.Children.Add(BuildThread(thread));

        if (_vm.HasMore)
        {
            var more = new Button { Content = "Load more comments", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            more.Click += async (_, _) => { more.IsEnabled = false; await _vm.LoadMoreAsync(); };
            ThreadList.Children.Add(more);
        }
    }

    private FrameworkElement BuildThread(CommentThread thread)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(BuildComment(thread.Top));

        var repliesHost = new StackPanel { Margin = new Thickness(28, 4, 0, 0) };
        foreach (var r in thread.PreviewReplies) repliesHost.Children.Add(BuildComment(r));
        stack.Children.Add(repliesHost);

        bool expanded = false;
        if (thread.TotalReplyCount > thread.PreviewReplies.Count)
        {
            var toggle = new Button
            {
                Content = $"View all {thread.TotalReplyCount} replies",
                Style = (Style)FindResource("Button.Icon"),
                Foreground = (Brush)FindResource("Brush.Accent"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = double.NaN,
                Margin = new Thickness(28, 2, 0, 0),
                FontSize = 12,
            };
            toggle.Click += async (_, _) =>
            {
                if (expanded) return;
                expanded = true;
                toggle.IsEnabled = false;
                var all = await _vm.LoadRepliesAsync(thread.Id);
                repliesHost.Children.Clear();
                foreach (var r in all) repliesHost.Children.Add(BuildComment(r));
                toggle.Visibility = Visibility.Collapsed;
            };
            stack.Children.Add(toggle);
        }

        // Inline reply box
        var replyBox = new TextBox { Margin = new Thickness(28, 6, 0, 0), Visibility = Visibility.Collapsed };
        var replyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(28, 6, 0, 0), Visibility = Visibility.Collapsed };
        var send = new Button { Content = "Reply", Padding = new Thickness(10, 4, 10, 4) };
        send.Click += async (_, _) =>
        {
            send.IsEnabled = false;
            if (await _vm.ReplyAsync(thread.Id, replyBox.Text))
            {
                replyBox.Clear();
                replyBox.Visibility = replyRow.Visibility = Visibility.Collapsed;
                NotificationCenterReport("Reply posted — it may take a moment to appear.");
            }
            send.IsEnabled = true;
        };
        replyRow.Children.Add(send);

        var replyLink = new Button
        {
            Content = "Reply",
            Style = (Style)FindResource("Button.Icon"),
            Foreground = (Brush)FindResource("Brush.TextSecondary"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = double.NaN,
            Margin = new Thickness(28, 2, 0, 0),
            FontSize = 12,
        };
        replyLink.Click += (_, _) =>
        {
            replyBox.Visibility = replyRow.Visibility = Visibility.Visible;
            replyBox.Focus();
        };
        stack.Children.Add(replyLink);
        stack.Children.Add(replyBox);
        stack.Children.Add(replyRow);

        return stack;
    }

    private FrameworkElement BuildComment(CommentInfo c)
    {
        var header = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
        header.Inlines.Add(new System.Windows.Documents.Run(c.Author) { FontWeight = FontWeights.SemiBold });
        header.Inlines.Add(new System.Windows.Documents.Run(
            "   " + FeedFormatting.RelativeDate(c.PublishedAt)) { Foreground = (Brush)FindResource("Brush.TextTertiary") });

        var body = new TextBlock
        {
            Text = c.Text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("Brush.TextPrimary"),
        };

        var meta = c.LikeCount > 0
            ? Meta($"↑ {c.LikeCount:N0}")
            : null;

        var col = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        col.Children.Add(header);
        col.Children.Add(body);
        if (meta != null) { meta.Margin = new Thickness(0, 2, 0, 0); col.Children.Add(meta); }
        return col;
    }

    private TextBlock Meta(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("Brush.TextTertiary"),
        FontSize = 12,
        Margin = new Thickness(0, 6, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    private static void NotificationCenterReport(string message) =>
        Diagnostics.NotificationCenter.Report(message);
}
