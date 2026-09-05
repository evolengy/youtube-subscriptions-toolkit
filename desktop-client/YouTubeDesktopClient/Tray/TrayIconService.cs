using System.Windows.Forms;

namespace YouTubeDesktopClient.Tray;

public class TrayIconService : IDisposable
{
    private const string StartupValueName = "YouTubeDesktopClient";
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(Action onOpen, Action onRefreshNow, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => onOpen());
        menu.Items.Add("Refresh now", null, (_, _) => onRefreshNow());

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
        menu.Items.Add(startupItem);

        menu.Items.Add("Exit", null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "YouTube Desktop Client",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    public void Dispose() => _notifyIcon.Dispose();
}
