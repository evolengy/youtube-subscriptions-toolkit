using System.Windows.Forms;
using YouTubeDesktopClient.Themes;

namespace YouTubeDesktopClient.Tray;

public class TrayIconService : IDisposable
{
    private const string StartupValueName = "YouTubeDesktopClient";
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;

    // Sign in / Sign out live in the toolbar account control now, not here.
    public TrayIconService(Action onOpen, Action onRefreshNow, Action onExit)
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open", null, (_, _) => onOpen());
        _menu.Items.Add("Refresh now", null, (_, _) => onRefreshNow());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled(StartupValueName),
        };
        startupItem.Click += (_, _) =>
        {
            var executablePath = Environment.ProcessPath!;
            if (startupItem.Checked)
                StartupRegistration.Enable(StartupValueName, executablePath);
            else
                StartupRegistration.Disable(StartupValueName);
        };
        _menu.Items.Add(startupItem);

        _menu.Items.Add("Exit", null, (_, _) => onExit());

        TrayMenuTheme.Apply(_menu);
        ThemeManager.ThemeChanged += OnThemeChanged;

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "YouTube Desktop Client",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    // ThemeChanged is raised on the UI thread (the toolbar toggle) or marshalled
    // to it (SystemEvents) — same STA thread the menu lives on.
    private void OnThemeChanged() => TrayMenuTheme.Apply(_menu);

    public void Dispose()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
