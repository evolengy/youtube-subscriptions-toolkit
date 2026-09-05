// App.xaml.cs
using System.IO;
using System.Net.Http;
using System.Windows;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Sync;
using YouTubeDesktopClient.Tray;

namespace YouTubeDesktopClient;

public partial class App : Application
{
    private const string OAuthClientId = "PASTE_DESKTOP_OAUTH_CLIENT_ID_HERE.apps.googleusercontent.com";
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(3);

    private TrayIconService? _tray;
    private BackgroundSyncService? _sync;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeSubscriptionsToolkit");
        var store = new SubscriptionStore(
            Path.Combine(appDataDir, "settings.json"),
            Path.Combine(appDataDir, "cache.json"));
        var tokenStore = new TokenStore(Path.Combine(appDataDir, "token.bin"));
        var authService = new AuthService(OAuthClientId, tokenStore);
        var apiClient = new YouTubeApiClient(new HttpClient());

        Func<Task<string?>> getAccessToken = () => authService.GetAccessTokenSilentAsync();

        _sync = new BackgroundSyncService(apiClient, store, getAccessToken);
        _sync.Start(SyncInterval);

        var groupsViewModel = new GroupsViewModel(store);
        var feedViewModel = new FeedViewModel(store);
        var channelViewModel = new ChannelManagementViewModel(apiClient, store, getAccessToken);

        _mainWindow = new MainWindow(groupsViewModel, feedViewModel, channelViewModel);
        _mainWindow.Show();

        _tray = new TrayIconService(
            onOpen: () => { _mainWindow.Show(); _mainWindow.WindowState = WindowState.Normal; },
            onRefreshNow: () => _sync.RunOnceAsync().GetAwaiter().GetResult(),
            onExit: () => Shutdown());

        _ = SignInIfNeededAsync(authService);
    }

    private async Task SignInIfNeededAsync(AuthService authService)
    {
        var token = await authService.GetAccessTokenSilentAsync();
        if (token == null)
            await authService.SignInInteractiveAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sync?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
