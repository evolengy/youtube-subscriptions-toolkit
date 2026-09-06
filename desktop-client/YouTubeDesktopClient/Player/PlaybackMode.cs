namespace YouTubeDesktopClient.Player;

/// <summary>How a video tab plays its video. Persisted as a string on
/// <see cref="Storage.Models.AppSettings"/>.</summary>
public enum PlaybackMode
{
    /// <summary>Native page: the official YouTube IFrame player from a local host
    /// page. No web sign-in, no API quota — but no watch-history write.</summary>
    Embed,

    /// <summary>The real youtube.com/watch page loaded in the tab. Writes watch
    /// history when the shared WebView2 profile is signed in.</summary>
    FullPage,
}
