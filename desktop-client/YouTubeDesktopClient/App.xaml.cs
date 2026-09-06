// App.xaml.cs
using System.IO;
using System.Net.Http;
using System.Windows;
using YouTubeDesktopClient.Account;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Settings;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Sync;
using YouTubeDesktopClient.Themes;
using YouTubeDesktopClient.Tray;

namespace YouTubeDesktopClient;

/// <summary>Forwards WPF binding-trace warnings/errors to the app log.</summary>
internal sealed class BindingErrorListener : System.Diagnostics.TraceListener
{
    private readonly System.Text.StringBuilder _buffer = new();

    public override void Write(string? message) => _buffer.Append(message);

    public override void WriteLine(string? message)
    {
        _buffer.Append(message);
        Logger.LogError($"WPF binding: {_buffer}");
        _buffer.Clear();
    }
}

public partial class App : Application
{
    private const string OAuthClientId = "REPLACE_WITH_YOUR_OAUTH_CLIENT_ID.apps.googleusercontent.com";
    // Google requires this even for a "Desktop app" client; per Google's own
    // docs it "is not treated as a secret" for installed apps. Paste the
    // value shown for the Desktop-type client in Google Cloud Console.
    private const string OAuthClientSecret = "REPLACE_WITH_YOUR_OAUTH_CLIENT_SECRET";
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(3);

    private TrayIconService? _tray;
    private BackgroundSyncService? _sync;
    private MainWindow? _mainWindow;
    private bool _syncStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface failures that otherwise only reach an attached debugger: an
        // unhandled exception on the UI thread, and every WPF data-binding
        // error (a mistyped Binding path, a missing DynamicResource key). Both
        // go to the same log file as everything else.
        DispatcherUnhandledException += (_, args) =>
            Logger.LogError("Unhandled UI-thread exception", args.Exception);
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorListener());
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;

        // Finish a sign-out's profile wipe that couldn't complete last run —
        // before any WebView2 environment is created against that folder.
        WebViewProfile.WipeIfPending();

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeSubscriptionsToolkit");
        var store = new SubscriptionStore(
            Path.Combine(appDataDir, "settings.json"),
            Path.Combine(appDataDir, "cache.json"));

        // Before any window is shown, so the first paint is already themed.
        ThemeManager.Initialize(store.GetAppSettings().Theme);
        var tokenStore = new TokenStore(Path.Combine(appDataDir, "token.bin"));
        var authService = new AuthService(OAuthClientId, OAuthClientSecret, tokenStore);
        var apiClient = new YouTubeApiClient(new HttpClient());

        Func<Task<string?>> getAccessToken = () => authService.GetAccessTokenSilentAsync();

        _sync = new BackgroundSyncService(apiClient, store, getAccessToken);

        var appSettingsViewModel = new AppSettingsViewModel(store);
        var feedExpansion = new FeedExpansionService(apiClient, store, getAccessToken);
        var groupsViewModel = new GroupsViewModel(store);
        var feedViewModel = new FeedViewModel(store, feedExpansion,
            isAutoExpandEnabled: () => appSettingsViewModel.AutoExpandFeed);
        var channelViewModel = new ChannelManagementViewModel(apiClient, store, getAccessToken);

        var account = new AccountViewModel(authService, apiClient, store, appDataDir,
            wipeWebViewProfile: () => _mainWindow?.WipeWebViewProfile());

        _mainWindow = new MainWindow(groupsViewModel, feedViewModel, channelViewModel,
            appSettingsViewModel, store, apiClient, getAccessToken, account,
            onRefreshNow: () => _ = RefreshNowAsync());
        _mainWindow.Show();

        // BackgroundSyncService raises SyncCompleted on the timer's threadpool
        // thread, so the panel refresh has to hop to the UI thread explicitly.
        _sync.SyncCompleted += () => _mainWindow.Dispatcher.Invoke(() => _mainWindow.RefreshPanels());

        // The store isn't pointed at an account until AccountViewModel resolves
        // one, so the sync (which writes cache.json) must not run before then.
        // Start it the first time an account becomes active, and refresh the
        // panels on every sign-in / sign-out.
        account.Changed += () => _mainWindow.Dispatcher.Invoke(() =>
        {
            if (account.IsSignedIn && !_syncStarted)
            {
                _syncStarted = true;
                _sync.Start(SyncInterval);
            }
            _mainWindow.RefreshPanels();
        });

        _tray = new TrayIconService(
            onOpen: () => { _mainWindow.Show(); _mainWindow.WindowState = WindowState.Normal; },
            // Fire-and-forget rather than .GetAwaiter().GetResult(): this runs
            // on the UI thread, where blocking on a task whose continuations
            // are posted back to the dispatcher deadlocks permanently.
            onRefreshNow: () => _ = RefreshNowAsync(),
            onExit: () => Shutdown());

        _ = StartupAccountAsync(account);
    }

    private async Task StartupAccountAsync(AccountViewModel account)
    {
        try
        {
            if (!await account.ResolveAsync())
                await account.SignInAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Startup account flow failed", ex);
            Diagnostics.NotificationCenter.Report("Could not sign in — see the log file for details.");
        }
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
            Diagnostics.NotificationCenter.Report($"Refresh failed: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sync?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
