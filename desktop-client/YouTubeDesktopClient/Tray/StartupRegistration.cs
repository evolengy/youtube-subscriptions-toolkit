using Microsoft.Win32;

namespace YouTubeDesktopClient.Tray;

public static class StartupRegistration
{
    /// <summary>The Run-key value name shared by the tray menu and the Settings
    /// panel — both toggle the same registry entry, so they must name it identically.</summary>
    public const string DefaultValueName = "YouTubeDesktopClient";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(valueName) != null;
    }

    public static void Enable(string valueName, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(valueName, $"\"{executablePath}\"");
    }

    public static void Disable(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
