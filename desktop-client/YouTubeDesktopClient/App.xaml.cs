// App.xaml.cs
using System.IO;
using System.Net.Http;
using System.Windows;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Sync;
using YouTubeDesktopClient.Tray;

namespace YouTubeDesktopClient;

public partial class App : Application
{
    private const string OAuthClientId = "REPLACE_WITH_YOUR_OAUTH_CLIENT_ID.apps.googleusercontent.com";
    // Google requires this even for a "Desktop app" client; per Google's own
    // docs it "is not treated as a secret" for installed apps. Paste the
    // value shown for the Desktop-type client in Google Cloud Console.
    private const string OAuthClientSecret = "PASTE_DESKTOP_OAUTH_CLIENT_SECRET_HERE";
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
        var authService = new AuthService(OAuthClientId, OAuthClientSecret, tokenStore);
        var apiClient = new YouTubeApiClient(new HttpClient());

        Func<Task<string?>> getAccessToken = () => authService.GetAccessTokenSilentAsync();

        _sync = new BackgroundSyncService(apiClient, store, getAccessToken);

        var groupsViewModel = new GroupsViewModel(store);
        var feedViewModel = new FeedViewModel(store);
        var channelViewModel = new ChannelManagementViewModel(apiClient, store, getAccessToken);

        _mainWindow = new MainWindow(groupsViewModel, feedViewModel, channelViewModel);
        _mainWindow.Show();

        // BackgroundSyncService raises SyncCompleted on the timer's threadpool
        // thread, so the panel refresh has to hop to the UI thread explicitly.
        _sync.SyncCompleted += () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.RefreshPanels());

        // Started only after the handler is attached: Start fires the first run
        // immediately (TimeSpan.Zero), and a first launch that completed its
        // sync before the subscription existed would leave the UI stale — the
        // exact staleness this event is here to fix.
        _sync.Start(SyncInterval);

        _tray = new TrayIconService(
            onOpen: () => { _mainWindow.Show(); _mainWindow.WindowState = WindowState.Normal; },
            // Fire-and-forget rather than .GetAwaiter().GetResult(): this runs
            // on the UI thread, where blocking on a task whose continuations
            // are posted back to the dispatcher deadlocks permanently.
            onRefreshNow: () => _ = RefreshNowAsync(),
            onSignIn: () => _ = SignInIfNeededAsync(authService),
            onSignOut: () => authService.SignOut(),
            onExit: () => Shutdown());

        _ = SignInIfNeededAsync(authService);
    }

    private async Task RefreshNowAsync()
    {
        try
        {
            await _sync!.RunOnceAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Manual refresh failed", ex);
            MessageBox.Show($"Refresh failed: {ex.Message}");
        }
    }

    private async Task SignInIfNeededAsync(AuthService authService)
    {
        try
        {
            var token = await authService.GetAccessTokenSilentAsync();
            if (token == null)
                await authService.SignInInteractiveAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Sign-in failed", ex);
            MessageBox.Show(
                $"Sign-in failed: {ex.Message}\n\nMake sure you've set a real OAuth client ID in App.xaml.cs.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sync?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
