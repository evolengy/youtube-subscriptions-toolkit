// Player/PlayerPageHost.cs
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Player;

public enum PlayerEventKind { Ready, State, Error }

/// <summary>One status message from the embedded IFrame player. For a
/// <see cref="PlayerEventKind.State"/> event <see cref="Code"/> is the YT player
/// state (0 = ended, 1 = playing, 2 = paused, …); for
/// <see cref="PlayerEventKind.Error"/> it is the YT error code.</summary>
public readonly record struct PlayerEvent(PlayerEventKind Kind, int Code)
{
    public bool IsEnded => Kind == PlayerEventKind.State && Code == 0;

    /// <summary>True when the error means "this video will never play in the
    /// embedded player" — the player page should offer to open it on YouTube
    /// instead. See <see cref="PlayerPageHost.IsUnembeddable"/>.</summary>
    public bool IsUnembeddable => Kind == PlayerEventKind.Error && PlayerPageHost.IsUnembeddable(Code);

    /// <summary>Parses a <c>window.chrome.webview.postMessage</c> payload from
    /// <c>player.html</c>. Returns null for anything unrecognised.</summary>
    public static PlayerEvent? Parse(string json)
    {
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeProp))
                return null;

            return typeProp.GetString() switch
            {
                "ready" => new PlayerEvent(PlayerEventKind.Ready, 0),
                "state" => new PlayerEvent(PlayerEventKind.State,
                    root.TryGetProperty("state", out var s) ? s.GetInt32() : -1),
                "error" => new PlayerEvent(PlayerEventKind.Error,
                    root.TryGetProperty("code", out var c) ? c.GetInt32() : 0),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The C# side of the <c>player.html</c> bridge. Maps the bundled player folder
/// to a stable <c>https://ytdesktop.local/</c> origin (so <c>iframe_api</c> loads
/// and postMessage has a real origin), navigates a tab's <see cref="WebView2"/>
/// to the player page, relays commands to it, and surfaces its status messages
/// as <see cref="Received"/> events keyed by tab id.
/// </summary>
public sealed class PlayerPageHost
{
    public const string VirtualHost = "ytdesktop.local";

    private readonly string _playerFolder;
    private readonly HashSet<CoreWebView2> _wired = new();
    private readonly Dictionary<CoreWebView2, string> _tabByCore = new();

    public PlayerPageHost(string playerFolder) => _playerFolder = playerFolder;

    /// <summary>Raised on the UI thread with (tabId, event) for every status
    /// message from a player page this host is driving.</summary>
    public event Action<string, PlayerEvent>? Received;

    public static string BuildUrl(string videoId) =>
        $"https://{VirtualHost}/player.html?v={Uri.EscapeDataString(videoId)}&autoplay=1";

    /// <summary>The canonical watch URL for a video — used for the "open on
    /// YouTube" fallback and for FullPage playback.</summary>
    public static string WatchUrl(string videoId) =>
        $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";

    /// <summary>
    /// Whether a YouTube IFrame <c>onError</c> code means the embedded player is a
    /// dead end for this video and the user should be offered the real watch page.
    /// </summary>
    /// <remarks>
    /// True for every code except <c>2</c>:
    /// <list type="bullet">
    /// <item><c>101</c>/<c>150</c> — owner blocked third-party embeds; the watch
    ///   page is unaffected, so the fallback genuinely fixes it.</item>
    /// <item><c>100</c> — removed/private; the watch page may still play it when
    ///   the shared profile is signed in (private-but-shared), and if not, a
    ///   clear "isn't available" overlay beats a bare error inside a black box.</item>
    /// <item><c>5</c> — HTML5 playback failure; rare, and the full page uses a
    ///   different player path that often works.</item>
    /// <item><c>2</c> — malformed video id, i.e. a bug on our side. The watch
    ///   page would fail the same way, so the fallback is pointless — just log it.</item>
    /// </list>
    /// </remarks>
    public static bool IsUnembeddable(int errorCode) => errorCode is 5 or 100 or 101 or 150;

    /// <summary>Navigates <paramref name="webView"/> (CoreWebView2 already
    /// initialised) to the player page for <paramref name="videoId"/>.</summary>
    public void Load(WebView2 webView, string tabId, string videoId)
    {
        var core = webView.CoreWebView2;
        if (core is null) return;

        _tabByCore[core] = tabId;
        if (_wired.Add(core))
        {
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost, _playerFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnWebMessage;
        }

        webView.Source = new Uri(BuildUrl(videoId));
    }

    public void Pause(WebView2? webView) => Post(webView, "pause");
    public void Play(WebView2? webView) => Post(webView, "play");

    private static void Post(WebView2? webView, string command)
    {
        try { webView?.CoreWebView2?.PostWebMessageAsString(command); }
        catch (Exception ex) { Logger.LogError($"Player command '{command}' failed", ex); }
    }

    /// <summary>Drops a disposed WebView2's wiring so the dictionaries don't leak.</summary>
    public void Forget(WebView2? webView)
    {
        if (webView?.CoreWebView2 is not { } core) return;
        core.WebMessageReceived -= OnWebMessage;
        _wired.Remove(core);
        _tabByCore.Remove(core);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !_tabByCore.TryGetValue(core, out var tabId))
            return;

        string json;
        try { json = e.WebMessageAsJson; }
        catch { return; }

        if (PlayerEvent.Parse(json) is { } evt)
            Received?.Invoke(tabId, evt);
    }
}
