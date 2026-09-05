// Logging/Logger.cs
namespace YouTubeDesktopClient.Logging;

public static class Logger
{
    private static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YouTubeSubscriptionsToolkit", "log.txt");

    public static void LogError(string message, Exception? ex = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            var line = $"[{DateTimeOffset.Now:u}] {message}{(ex != null ? $" — {ex.GetType().Name}: {ex.Message}" : "")}{Environment.NewLine}";
            System.IO.File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Logging must never itself throw and take down the process it's trying to protect.
        }
    }
}
