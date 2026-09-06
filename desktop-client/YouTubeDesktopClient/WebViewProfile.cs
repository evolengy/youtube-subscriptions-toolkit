// WebViewProfile.cs
using System.IO;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient;

/// <summary>
/// The shared youtube.com session lives in <c>%AppData%\...\WebView2\</c>. Sign-out
/// needs to clear it, but the folder is locked by msedgewebview2 child processes
/// that linger for a moment after the WebView2s are disposed. So sign-out just
/// drops a marker; <see cref="WipeIfPending"/> runs at the very start of the next
/// launch — before any WebView2 environment is created — when the folder is free.
/// An opportunistic immediate delete is also attempted (it succeeds in the common
/// case where the app is exited cleanly right after signing out).
/// </summary>
internal static class WebViewProfile
{
    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YouTubeSubscriptionsToolkit");

    private static string Folder => Path.Combine(Root, "WebView2");
    private static string Marker => Path.Combine(Root, ".webview-wipe-pending");

    public static void RequestWipe()
    {
        try { File.WriteAllText(Marker, DateTimeOffset.UtcNow.ToString("u")); }
        catch (Exception ex) { Logger.LogError("Could not mark the WebView2 profile for wiping", ex); }
        TryDeleteFolder(); // often works immediately; if not, next launch finishes it
    }

    public static void WipeIfPending()
    {
        if (!File.Exists(Marker)) return;
        if (TryDeleteFolder())
        {
            try { File.Delete(Marker); } catch { /* retried next launch */ }
        }
    }

    private static bool TryDeleteFolder()
    {
        if (!Directory.Exists(Folder)) return true;
        try
        {
            Directory.Delete(Folder, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("WebView2 profile still locked; will retry next launch", ex);
            return false;
        }
    }
}
