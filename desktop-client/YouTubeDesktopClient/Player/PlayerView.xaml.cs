// Player/PlayerView.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Player;

/// <summary>
/// The native video page: the active tab's <see cref="WebView2"/> on top, a
/// native title / channel / views·date / description strip below it, and an
/// overlay offering "open on YouTube" when the embedded player rejects a video.
/// In <see cref="ShowFullPage"/> mode the WebView fills everything and the strip
/// is hidden — the real youtube.com page already carries that information.
/// </summary>
public partial class PlayerView : UserControl
{
    private readonly VideoActionsViewModel _actions;
    private readonly Action<string> _onOpenOnYouTube;   // navigate this tab's WebView2 to the watch page

    private string _videoId = "";
    private string _tabTitle = "";

    public PlayerView(VideoActionsViewModel actions, Action<string> onOpenOnYouTube)
    {
        InitializeComponent();
        _actions = actions;
        _onOpenOnYouTube = onOpenOnYouTube;
        _actions.MetadataLoaded += RefreshMeta;
    }

    /// <summary>Embed mode: show the player page for <paramref name="videoId"/>
    /// plus the native metadata strip. <paramref name="tabTitle"/> is the heading
    /// until (and if) the API returns richer metadata.</summary>
    public void ShowEmbed(WebView2 webView, string videoId, string tabTitle)
    {
        _videoId = videoId;
        _tabTitle = tabTitle;
        Reparent(webView);
        FallbackOverlay.Visibility = Visibility.Collapsed;
        MetaStrip.Visibility = Visibility.Visible;
        RefreshMeta();
    }

    /// <summary>FullPage mode: the WebView is the whole view.</summary>
    public void ShowFullPage(WebView2 webView)
    {
        Reparent(webView);
        FallbackOverlay.Visibility = Visibility.Collapsed;
        MetaStrip.Visibility = Visibility.Collapsed;
    }

    /// <summary>Called when the embedded player reports it can't play the current
    /// video — offer the YouTube fallbacks.</summary>
    public void ShowUnembeddable() => FallbackOverlay.Visibility = Visibility.Visible;

    private void Reparent(WebView2 webView)
    {
        if (ReferenceEquals(WebHost.Content, webView)) return;
        (webView.Parent as ContentControl)?.SetCurrentValue(ContentControl.ContentProperty, null);
        WebHost.Content = webView;
    }

    /// <summary>Drops the WebView2 from the host if it's the one currently shown,
    /// so a disposed instance isn't left parented.</summary>
    public void DetachIfShowing(WebView2 webView)
    {
        if (ReferenceEquals(WebHost.Content, webView)) WebHost.Content = null;
    }

    private void RefreshMeta()
    {
        TitleText.Text = string.IsNullOrWhiteSpace(_tabTitle) ? "Video" : _tabTitle;

        var channel = _actions.ChannelTitle;
        var views = _actions.ViewCount > 0 ? $"{_actions.ViewCount:N0} views" : null;
        var date = _actions.PublishedAt is { } p ? FeedFormatting.RelativeDate(p) : null;
        var tail = string.Join("  ·  ", new[] { views, date }.Where(s => !string.IsNullOrEmpty(s)));

        SubText.Text = string.IsNullOrEmpty(channel)
            ? tail
            : string.IsNullOrEmpty(tail) ? channel : $"{channel}  ·  {tail}";
        SubText.Visibility = SubText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        DescriptionText.Text = _actions.Description;
        DescriptionScroller.Visibility = string.IsNullOrWhiteSpace(_actions.Description)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenOnYouTube_Click(object sender, RoutedEventArgs e) => _onOpenOnYouTube(_videoId);

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PlayerPageHost.WatchUrl(_videoId)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogError("Opening the video in the browser failed", ex);
        }
    }
}
