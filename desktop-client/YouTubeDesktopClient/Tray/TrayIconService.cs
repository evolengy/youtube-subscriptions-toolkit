using System.Windows.Forms;
using YouTubeDesktopClient.Logging;
using YouTubeDesktopClient.Themes;

namespace YouTubeDesktopClient.Tray;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Drawing.Icon? _appIcon;

    // Sign in / Sign out live in the toolbar account control now, not here.
    public TrayIconService(Action onOpen, Action onRefreshNow, Action onExit)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open", null, (_, _) => onOpen());
        _menu.Items.Add("Refresh now", null, (_, _) => onRefreshNow());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled(StartupRegistration.DefaultValueName),
        };
        startupItem.Click += (_, _) =>
        {
            var executablePath = Environment.ProcessPath!;
            if (startupItem.Checked)
                StartupRegistration.Enable(StartupRegistration.DefaultValueName, executablePath);
            else
                StartupRegistration.Disable(StartupRegistration.DefaultValueName);
        };
        // The Settings panel toggles the same registry entry, so re-read it each
        // time the menu opens rather than trusting the value captured at startup.
        _menu.Opening += (_, _) =>
            startupItem.Checked = StartupRegistration.IsEnabled(StartupRegistration.DefaultValueName);
        _menu.Items.Add(startupItem);

        _menu.Items.Add("Exit", null, (_, _) => onExit());

        TrayMenuTheme.Apply(_menu);
        ThemeManager.ThemeChanged += OnThemeChanged;

        _appIcon = TryLoadAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "YouTube Desktop Client",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    // The tray icon is the same one <ApplicationIcon> stamped onto the .exe, so
    // pull it straight off the running executable instead of shipping a copy.
    private static System.Drawing.Icon? TryLoadAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            return exePath is null ? null : System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch (Exception ex)
        {
            Logger.LogError("Could not load the app icon for the tray", ex);
            return null;
        }
    }

    // ThemeChanged is raised on the UI thread (the toolbar toggle) or marshalled
    // to it (SystemEvents) — same STA thread the menu lives on.
    private void OnThemeChanged() => TrayMenuTheme.Apply(_menu);

    public void Dispose()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _appIcon?.Dispose();
    }
}
