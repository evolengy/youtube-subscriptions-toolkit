// MainWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Tabs;

namespace YouTubeDesktopClient;

public partial class MainWindow : Window
{
    private readonly TabsViewModel _tabs = new();
    private readonly Dictionary<string, WebView2> _webViewsByTabId = new();

    public MainWindow(GroupsViewModel groupsViewModel, FeedViewModel feedViewModel,
        ChannelManagementViewModel channelViewModel)
    {
        InitializeComponent();
        GroupsHost.Content = new Views.GroupsPanel(groupsViewModel, OnGroupSelected);
        RebuildTabStrip(feedViewModel, channelViewModel);
    }

    private void RebuildTabStrip(FeedViewModel feedViewModel, ChannelManagementViewModel channelViewModel)
    {
        MainTabs.Items.Clear();
        foreach (var tab in _tabs.Tabs)
        {
            var tabItem = new TabItem { Header = tab.Title, Tag = tab.Id };
            tabItem.Content = tab.Id switch
            {
                "feed" => new Views.FeedPanel(feedViewModel, videoId => OpenVideo(videoId, forceNewTab: false)),
                "home" => GetOrCreateWebView(tab.Id, tab.Url),
                _ => GetOrCreateWebView(tab.Id, tab.Url),
            };
            MainTabs.Items.Add(tabItem);
        }
    }

    private WebView2 GetOrCreateWebView(string tabId, string url)
    {
        if (_webViewsByTabId.TryGetValue(tabId, out var existing))
        {
            existing.Source = new Uri(url);
            return existing;
        }
        var webView = new WebView2();
        webView.EnsureCoreWebView2Async().ContinueWith(_ =>
            Dispatcher.Invoke(() => webView.Source = new Uri(url)));
        _webViewsByTabId[tabId] = webView;
        return webView;
    }

    private void OpenVideo(string videoId, bool forceNewTab)
    {
        _tabs.NavigateActiveOrNewTab(videoId, forceNewTab);
        RebuildTabStripPreservingFeedAndHome();
    }

    private void RebuildTabStripPreservingFeedAndHome()
    {
        // Re-render only the tab strip's items to match _tabs.Tabs; existing
        // WebView2 instances are reused via _webViewsByTabId so navigating
        // doesn't recreate the browser process for tabs that already exist.
        foreach (var tab in _tabs.Tabs.Where(t => !t.IsPinned))
        {
            if (MainTabs.Items.Cast<TabItem>().Any(ti => (string)ti.Tag == tab.Id)) continue;
            var tabItem = new TabItem { Header = tab.Title, Tag = tab.Id, Content = GetOrCreateWebView(tab.Id, tab.Url) };
            MainTabs.Items.Add(tabItem);
        }
        var activeItem = MainTabs.Items.Cast<TabItem>().FirstOrDefault(ti => (string)ti.Tag == _tabs.ActiveTabId);
        if (activeItem != null) MainTabs.SelectedItem = activeItem;

        // If the active video tab was reused in place, its WebView2 needs
        // the new URL explicitly, since GetOrCreateWebView above only runs
        // for *new* tabs.
        var activeTab = _tabs.Tabs.First(t => t.Id == _tabs.ActiveTabId);
        if (_webViewsByTabId.TryGetValue(activeTab.Id, out var webView))
            webView.Source = new Uri(activeTab.Url);
    }

    private void OnGroupSelected(string? groupId)
    {
        var feedTab = MainTabs.Items.Cast<TabItem>().First(ti => (string)ti.Tag == "feed");
        ((Views.FeedPanel)feedTab.Content).SetActiveGroup(groupId);
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedItem is TabItem { Tag: string tabId }) _tabs.ActiveTabId = tabId;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
