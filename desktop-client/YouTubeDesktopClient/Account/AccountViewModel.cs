// Account/AccountViewModel.cs
using System.IO;
using System.Text.Json;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Storage;

namespace YouTubeDesktopClient.Account;

/// <summary>
/// The single source of truth for "who is signed in". Wraps <see cref="AuthService"/>
/// and the account read, and points <see cref="SubscriptionStore"/> at the active
/// account's folder so each account keeps its own groups / watched / cache.
///
/// Switching to a *different* account mid-session relaunches the app — every view
/// model and <see cref="Themes.ThemeManager"/> captured the previous account's
/// settings at construction, and a clean restart rebinds them all. The common
/// paths (startup resolve, first sign-in, sign-out) never restart.
/// </summary>
public sealed class AccountViewModel
{
    private readonly IAuthService _auth;
    private readonly IYouTubeAccountApi _api;
    private readonly SubscriptionStore _store;
    private readonly string _appDataDir;
    private readonly Action _wipeWebViewProfile;
    private readonly Action _relaunch;

    public AccountViewModel(IAuthService auth, IYouTubeAccountApi api, SubscriptionStore store,
        string appDataDir, Action wipeWebViewProfile, Action? relaunch = null)
    {
        _auth = auth;
        _api = api;
        _store = store;
        _appDataDir = appDataDir;
        _wipeWebViewProfile = wipeWebViewProfile;
        _relaunch = relaunch ?? RelaunchProcess;
    }

    public MyChannel? Channel { get; private set; }
    public bool IsSignedIn => Channel != null;

    /// <summary>Raised (on the calling thread) after the signed-in state changes.</summary>
    public event Action? Changed;

    private string AccountFilePath => Path.Combine(_appDataDir, "account.json");

    /// <summary>Startup: silent token → resolve the channel → activate its store.
    /// If the token is valid but the identity call fails (e.g. quota exhausted),
    /// falls back to the last-known account so the app stays signed in offline.
    /// Returns true when an account is now active.</summary>
    public async Task<bool> ResolveAsync()
    {
        var token = await _auth.GetAccessTokenSilentAsync();
        if (token is null) { SetSignedOut(); return false; }

        var me = await SafeGetChannelAsync(token) ?? LoadCachedChannel();
        if (me is null) { SetSignedOut(); return false; }

        Activate(me);
        return true;
    }

    private MyChannel? LoadCachedChannel()
    {
        try
        {
            return File.Exists(AccountFilePath)
                ? JsonSerializer.Deserialize<MyChannel>(File.ReadAllText(AccountFilePath))
                : null;
        }
        catch (Exception ex)
        {
            Logger.LogError("Reading the cached account failed", ex);
            return null;
        }
    }

    private void SaveCachedChannel(MyChannel me)
    {
        try { File.WriteAllText(AccountFilePath, JsonSerializer.Serialize(me)); }
        catch (Exception ex) { Logger.LogError("Caching the account failed", ex); }
    }

    /// <summary>Interactive sign-in. Relaunches the app if it switches accounts.</summary>
    public async Task SignInAsync()
    {
        var previousId = Channel?.ChannelId;
        try
        {
            var token = await _auth.SignInInteractiveAsync();
            var me = await SafeGetChannelAsync(token);
            if (me is null) { SetSignedOut(); return; }

            if (previousId is not null && previousId != me.ChannelId)
            {
                _relaunch(); // the refresh token is already saved; relaunch resolves it cleanly
                return;
            }
            Activate(me);
        }
        catch (Exception ex)
        {
            Logger.LogError("Interactive sign-in failed", ex);
            NotificationCenter.Report("Sign-in failed — see the log file for details.");
            SetSignedOut();
        }
    }

    /// <summary>Clears the API token and the youtube.com session; each account's
    /// stored data stays in its folder for the next sign-in.</summary>
    public Task SignOutAsync()
    {
        _auth.SignOut();
        _store.Deactivate();
        try { _wipeWebViewProfile(); }
        catch (Exception ex) { Logger.LogError("Wiping the WebView2 profile on sign-out failed", ex); }
        SetSignedOut();
        return Task.CompletedTask;
    }

    private void Activate(MyChannel me)
    {
        Channel = me;
        _store.ActivateAccount(me.ChannelId, _appDataDir);
        SaveCachedChannel(me);
        Changed?.Invoke();
    }

    private void SetSignedOut()
    {
        Channel = null;
        try { if (File.Exists(AccountFilePath)) File.Delete(AccountFilePath); }
        catch (Exception ex) { Logger.LogError("Clearing the cached account failed", ex); }
        Changed?.Invoke();
    }

    private async Task<MyChannel?> SafeGetChannelAsync(string token)
    {
        try
        {
            return await _api.GetMyChannelAsync(token);
        }
        catch (Exception ex)
        {
            Logger.LogError("Fetching the signed-in channel failed", ex);
            return null;
        }
    }

    private static void RelaunchProcess()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current?.Shutdown();
    }
}
