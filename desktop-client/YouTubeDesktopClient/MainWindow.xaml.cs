// MainWindow.xaml.cs
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Player;
using YouTubeDesktopClient.Settings;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Tabs;
using YouTubeDesktopClient.Themes;

namespace YouTubeDesktopClient;

public partial class MainWindow : Window
{
    private readonly TabsViewModel _tabs = new();
    private readonly Dictionary<string, WebView2> _webViewsByTabId = new();
    // Which player each open tab is using. Set when a video is opened (from the
    // PlaybackMode setting) and flipped to FullPage if the embed player rejects
    // the video. Cleaned alongside _webViewsByTabId in PruneWebViews.
    private readonly Dictionary<string, Player.PlaybackMode> _tabPlaybackMode = new();
    private readonly AppSettingsViewModel _settingsViewModel;
    private readonly Action _onRefreshNow;

    private readonly Views.FeedPanel _feedPanel;
    private readonly Views.ChannelManagementPanel _channelPanel;
    private readonly Views.SettingsPanel _settingsPanel;
    private WebView2? _homeWebView;

    // The video player = the API action bar stacked on top of PlayerView (the
    // WebView2 + native metadata strip + "open on YouTube" fallback). One reused
    // host; the WebView2 inside PlayerView is swapped per active tab.
    private readonly VideoActionsViewModel _actionsVm;
    private readonly Views.VideoActionBar _actionBar;
    private readonly Player.PlayerView _playerView;
    private readonly Player.PlayerPageHost _playerPageHost;
    private readonly DockPanel _playerHost = new();

    // The WebView2 currently shown in the player — paused when the user navigates
    // away (sidebar destination or a different video tab).
    private WebView2? _activePlayerWebView;

    // One shared WebView2 environment with a persistent user-data folder, so a
    // sign-in done once on youtube.com carries across every player tab and app
    // restarts (cookies live under %AppData%\...\WebView2).
    private CoreWebView2Environment? _webViewEnv;

    // Guards NavList.SelectionChanged while we clear/restore the selection
    // programmatically (showing a video tab deselects every nav item).
    private bool _navGuard;

    // The sidebar destination to fall back to when the last video tab closes.
    private string _destination = "feed";

    public MainWindow(GroupsViewModel groupsViewModel, FeedViewModel feedViewModel,
        ChannelManagementViewModel channelViewModel, AppSettingsViewModel settingsViewModel,
        SubscriptionStore store, IYouTubeApiClient apiClient, Func<Task<string?>> getAccessToken,
        Action onRefreshNow)
    {
        InitializeComponent();
        _settingsViewModel = settingsViewModel;
        _onRefreshNow = onRefreshNow;

        _actionsVm = new VideoActionsViewModel(apiClient, getAccessToken);
        _actionBar = new Views.VideoActionBar(_actionsVm);

        var playerFolder = Path.Combine(AppContext.BaseDirectory, "Player");
        _playerPageHost = new Player.PlayerPageHost(playerFolder);
        _playerPageHost.Received += OnPlayerEvent;
        _playerView = new Player.PlayerView(_actionsVm, onOpenOnYouTube: OpenActiveTabOnYouTube);

        DockPanel.SetDock(_actionBar, Dock.Top);
        _playerHost.Children.Add(_actionBar);
        _playerHost.Children.Add(_playerView); // fills the rest

        // Feed panel is built before GroupsPanel so an early group-selection
        // callback can't hit a null field.
        _feedPanel = new Views.FeedPanel(feedViewModel,
            (videoId, title) => OpenVideo(videoId, title, forceNewTab: false), settingsViewModel);
        _channelPanel = new Views.ChannelManagementPanel(channelViewModel);
        _settingsPanel = new Views.SettingsPanel(settingsViewModel, store);

        GroupsHost.Content = new Views.GroupsPanel(groupsViewModel, OnGroupSelected);

        UpdateThemeToggleLabel();
        ThemeManager.ThemeChanged += UpdateThemeToggleLabel;

        NotificationCenter.Changed += (_, _) => UpdateLogButton();
        UpdateLogButton();

        // Native title-bar + system menu follow the app theme (WPF doesn't do
        // this on its own).
        ThemeManager.ThemeChanged += ApplyWindowChrome;

        SelectNav("feed");
        RefreshVideoTabs();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowChrome();
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyWindowChrome()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // not shown yet; OnSourceInitialized will retry
        int dark = ThemeManager.IsDark ? 1 : 0;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Win10 20H1+/Win11; 19 on the very
        // first 20H1 builds. Darkens the caption buttons and the system menu.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    // ---- toolbar --------------------------------------------------------

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _onRefreshNow();

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e) => _settingsViewModel.CycleTheme();

    // ---- notifications -------------------------------------------------

    private void UpdateLogButton()
    {
        var count = NotificationCenter.Count;
        LogButton.Foreground = count > 0
            ? (Brush)FindResource("Brush.Danger")
            : (Brush)FindResource("Brush.TextSecondary");
        LogButton.ToolTip = count > 0 ? $"Notifications ({count})" : "Notifications";
        if (LogPopup.IsOpen) RefreshLogList();
    }

    private void LogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogList();
        LogPopup.IsOpen = !LogPopup.IsOpen;
    }

    private void RefreshLogList()
    {
        var items = NotificationCenter.Items
            .Select(n => new { n.Message, TimeText = n.Time.LocalDateTime.ToString("HH:mm:ss") })
            .ToList();
        LogList.ItemsSource = items;
        LogEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        NotificationCenter.Clear();
        RefreshLogList();
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeSubscriptionsToolkit", "log.txt");
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                NotificationCenter.Report("No log file yet.");
        }
        catch (Exception ex)
        {
            Logger.LogError("Opening the log file failed", ex);
        }
    }

    private void UpdateThemeToggleLabel()
    {
        (string glyphKey, string tip) = ThemeManager.Selected switch
        {
            AppTheme.Light => ("Glyph.ThemeLight", "Theme: Light — click for Dark"),
            AppTheme.Dark => ("Glyph.ThemeDark", "Theme: Dark — click for Auto"),
            _ => ("Glyph.ThemeSystem", "Theme: Auto (follows Windows) — click for Light"),
        };
        ThemeToggleButton.Content = (string)FindResource(glyphKey);
        ThemeToggleButton.ToolTip = tip;
    }

    // ---- sidebar destinations ------------------------------------------

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navGuard) return;
        if (NavList.SelectedItem is ListBoxItem { Tag: string dest }) ShowDestination(dest);
    }

    private void SelectNav(string dest)
    {
        _navGuard = true;
        NavList.SelectedItem = NavList.Items.Cast<ListBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == dest);
        _navGuard = false;
        ShowDestination(dest);
    }

    private void ShowDestination(string dest)
    {
        PauseActivePlayer();
        _destination = dest;
        _tabs.ActiveTabId = null;
        RefreshVideoTabs();

        ContentHost.Content = dest switch
        {
            "home" => GetOrCreateHomeWebView(),
            "channels" => _channelPanel,
            "settings" => _settingsPanel,
            _ => _feedPanel,
        };
    }

    private void OnGroupSelected(string? groupId)
    {
        _feedPanel.SetActiveGroup(groupId);
        SelectNav("feed");
    }

    // ---- video tabs ----------------------------------------------------

    private void OpenVideo(string videoId, string? title, bool forceNewTab)
    {
        var tab = _tabs.OpenVideo(videoId, title, forceNewTab);
        _tabPlaybackMode[tab.Id] = _settingsViewModel.PlaybackMode;
        GetOrCreateVideoWebView(tab);
        ShowActiveVideo();
    }

    private Player.PlaybackMode ModeFor(string tabId) =>
        _tabPlaybackMode.GetValueOrDefault(tabId, Player.PlaybackMode.Embed);

    /// <summary>Navigates the active tab away from the (failed) embed player to
    /// the real watch page — the PlayerView fallback's "Open on YouTube".</summary>
    private void OpenActiveTabOnYouTube(string videoId)
    {
        if (_tabs.ActiveTabId is not string id || !_webViewsByTabId.TryGetValue(id, out var wv))
            return;
        _tabPlaybackMode[id] = Player.PlaybackMode.FullPage;
        if (wv.CoreWebView2 != null) wv.Source = new Uri(Player.PlayerPageHost.WatchUrl(videoId));
        _playerView.ShowFullPage(wv);
    }

    private void OnPlayerEvent(string tabId, Player.PlayerEvent evt)
    {
        if (evt.IsUnembeddable && tabId == _tabs.ActiveTabId)
            _playerView.ShowUnembeddable();
    }

    private void PauseActivePlayer()
    {
        if (_activePlayerWebView is { } wv) _playerPageHost.Pause(wv);
    }

    private void ActivateTab(string tabId)
    {
        _tabs.ActiveTabId = tabId;
        ShowActiveVideo();
    }

    private void ShowActiveVideo()
    {
        _navGuard = true;
        NavList.SelectedItem = null;
        _navGuard = false;
        RefreshVideoTabs();
        ShowActiveVideoContent();
    }

    /// <summary>Puts the action bar + the active tab's WebView2 into the content host.</summary>
    private void ShowActiveVideoContent()
    {
        if (_tabs.ActiveTabId is not string id ||
            !_webViewsByTabId.TryGetValue(id, out var wv) ||
            _tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab)
            return;

        if (!ReferenceEquals(_activePlayerWebView, wv)) PauseActivePlayer();
        _activePlayerWebView = wv;

        if (ModeFor(id) == Player.PlaybackMode.Embed)
            _playerView.ShowEmbed(wv, tab.VideoId, tab.Title);
        else
            _playerView.ShowFullPage(wv);

        _actionBar.ShowVideo(tab.VideoId);
        ContentHost.Content = _playerHost;
    }

    private void CloseVideoTab(string tabId)
    {
        _tabs.CloseTab(tabId);
        AfterTabsChanged();
    }

    private void AfterTabsChanged()
    {
        PruneWebViews();
        RefreshVideoTabs();
        if (_tabs.ActiveTabId is string id && _webViewsByTabId.ContainsKey(id))
        {
            _navGuard = true;
            NavList.SelectedItem = null;
            _navGuard = false;
            ShowActiveVideoContent();
        }
        else
        {
            SelectNav(_destination);
        }
    }

    private void RefreshVideoTabs()
    {
        VideoTabStrip.Items.Clear();
        foreach (var tab in _tabs.Tabs) VideoTabStrip.Items.Add(BuildTabChip(tab));
    }

    private FrameworkElement BuildTabChip(TabInfo tab)
    {
        bool active = tab.Id == _tabs.ActiveTabId;

        var title = new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 170,
        };

        var close = new Button
        {
            Content = (string)FindResource("Glyph.Close"),
            Style = (Style)FindResource("Button.Icon"),
            Width = 20,
            Height = 20,
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close tab",
            Focusable = false,
            FocusVisualStyle = null,
        };
        close.Click += (_, _) => CloseVideoTab(tab.Id);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(title);

        var chip = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(10, 5, 6, 5),
            Margin = new Thickness(2, 0, 2, 0),
            // Active tab merges into the content canvas below and gets an accent
            // underline; inactive tabs are flat on the toolbar surface.
            Background = active ? (Brush)FindResource("Brush.Canvas") : Brushes.Transparent,
            BorderBrush = active ? (Brush)FindResource("Brush.Accent") : Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
        };
        if (!active)
        {
            chip.MouseEnter += (_, _) => chip.Background = (Brush)FindResource("Brush.SurfaceHover");
            chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        }
        chip.MouseLeftButtonUp += (_, _) => ActivateTab(tab.Id);
        chip.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle) CloseVideoTab(tab.Id);
        };
        chip.ContextMenu = BuildTabMenu(tab.Id);
        return chip;
    }

    private ContextMenu BuildTabMenu(string tabId)
    {
        var menu = new ContextMenu();

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) => CloseVideoTab(tabId);

        var others = new MenuItem { Header = "Close others" };
        others.Click += (_, _) => { _tabs.CloseOthers(tabId); AfterTabsChanged(); };

        var all = new MenuItem { Header = "Close all" };
        all.Click += (_, _) => { _tabs.CloseAll(); AfterTabsChanged(); };

        menu.Items.Add(close);
        menu.Items.Add(others);
        menu.Items.Add(all);
        return menu;
    }

    // ---- WebView2 lifecycle -------------------------------------------

    private async Task<CoreWebView2Environment> GetWebViewEnvAsync()
    {
        if (_webViewEnv != null) return _webViewEnv;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeSubscriptionsToolkit", "WebView2");
        // --autoplay-policy: let the IFrame player start on its own so a video
        // tab begins playing the moment it opens, without a synthetic click.
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
        };
        _webViewEnv = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null, userDataFolder: folder, options);
        return _webViewEnv;
    }

    /// <summary>
    /// Navigating a WebView2 before EnsureCoreWebView2Async(env) silently binds it
    /// to the DEFAULT environment, losing the shared profile — so new views are
    /// initialised against the shared env first, then navigated.
    /// </summary>
    private async Task InitAndNavigateAsync(WebView2 webView, string url)
    {
        try
        {
            var env = await GetWebViewEnvAsync();
            await webView.EnsureCoreWebView2Async(env);
            webView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            Logger.LogError("WebView2 init failed", ex);
        }
    }

    private WebView2 GetOrCreateVideoWebView(TabInfo tab)
    {
        if (_webViewsByTabId.TryGetValue(tab.Id, out var existing))
        {
            _ = NavigateForModeAsync(existing, tab);
            return existing;
        }
        var webView = new WebView2();
        _webViewsByTabId[tab.Id] = webView;
        _ = NavigateForModeAsync(webView, tab);
        return webView;
    }

    /// <summary>
    /// Points a tab's WebView2 at either the local embed player page or the real
    /// youtube.com/watch page, per the tab's <see cref="Player.PlaybackMode"/>.
    /// EnsureCoreWebView2Async(env) must run before any navigation or the view
    /// silently binds the default environment (see <see cref="InitAndNavigateAsync"/>).
    /// </summary>
    private async Task NavigateForModeAsync(WebView2 webView, TabInfo tab)
    {
        try
        {
            var env = await GetWebViewEnvAsync();
            await webView.EnsureCoreWebView2Async(env);
            if (ModeFor(tab.Id) == Player.PlaybackMode.Embed)
                _playerPageHost.Load(webView, tab.Id, tab.VideoId);
            else
                webView.Source = new Uri(tab.Url);
        }
        catch (Exception ex)
        {
            Logger.LogError("WebView2 init failed", ex);
        }
    }

    private WebView2 GetOrCreateHomeWebView()
    {
        if (_homeWebView != null) return _homeWebView;
        _homeWebView = new WebView2();
        _ = InitAndNavigateAsync(_homeWebView, "https://www.youtube.com/");
        return _homeWebView;
    }

    private void DisposeWebView(string tabId)
    {
        if (!_webViewsByTabId.Remove(tabId, out var wv)) return;
        _tabPlaybackMode.Remove(tabId);
        if (ReferenceEquals(ContentHost.Content, wv)) ContentHost.Content = null;
        _playerView.DetachIfShowing(wv);
        _playerPageHost.Forget(wv);
        if (ReferenceEquals(_activePlayerWebView, wv)) _activePlayerWebView = null;
        wv.Dispose();
    }

    private void PruneWebViews()
    {
        var orphans = _webViewsByTabId.Keys
            .Where(k => _tabs.Tabs.All(t => t.Id != k))
            .ToList();
        foreach (var id in orphans) DisposeWebView(id);
    }

    // ---- store-backed panel refresh ----------------------------------

    /// <summary>
    /// Re-renders the store-backed panels after a background sync has written new
    /// data. Caller must be on the UI thread (App marshals via Dispatcher.Invoke).
    /// </summary>
    public void RefreshPanels()
    {
        _feedPanel.Refresh();
        _channelPanel.Refresh();
        _settingsPanel.Refresh();
    }

    // ---- window chrome ----------------------------------------------

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
