// Views/SettingsPanel.xaml.cs
using System.Windows.Controls;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Player;
using YouTubeDesktopClient.Settings;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Themes;
using YouTubeDesktopClient.Tray;

namespace YouTubeDesktopClient.Views;

public partial class SettingsPanel : UserControl
{
    private readonly AppSettingsViewModel _settings;
    private readonly SubscriptionStore _store;
    private bool _loading;

    public SettingsPanel(AppSettingsViewModel settings, SubscriptionStore store)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        LoadFromSettings();

        ThemeLight.Checked += (_, _) => SetTheme(AppTheme.Light);
        ThemeDark.Checked += (_, _) => SetTheme(AppTheme.Dark);
        ThemeSystem.Checked += (_, _) => SetTheme(AppTheme.System);
        DensityComfortable.Checked += (_, _) => SetDensity(FeedDensity.Comfortable);
        DensityCompact.Checked += (_, _) => SetDensity(FeedDensity.Compact);
        PlaybackEmbed.Checked += (_, _) => Apply(() => _settings.PlaybackMode = PlaybackMode.Embed);
        PlaybackFullPage.Checked += (_, _) => Apply(() => _settings.PlaybackMode = PlaybackMode.FullPage);
        AutoExpandBox.Checked += (_, _) => Apply(() => _settings.AutoExpandFeed = true);
        AutoExpandBox.Unchecked += (_, _) => Apply(() => _settings.AutoExpandFeed = false);
        StartWithWindowsBox.Checked += (_, _) => Apply(() => SetStartWithWindows(true));
        StartWithWindowsBox.Unchecked += (_, _) => Apply(() => SetStartWithWindows(false));
    }

    /// <summary>Re-reads mutable status (last-synced time). Called on SyncCompleted.</summary>
    public void Refresh()
    {
        var last = _store.GetLastSyncedAt();
        LastSyncedText.Text = last == null
            ? "Not synced yet."
            : $"Last synced {last.Value.LocalDateTime:g}.";
    }

    private void LoadFromSettings()
    {
        _loading = true;
        (_settings.Theme switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem,
        }).IsChecked = true;
        (_settings.Density == FeedDensity.Compact ? DensityCompact : DensityComfortable).IsChecked = true;
        (_settings.PlaybackMode == PlaybackMode.FullPage ? PlaybackFullPage : PlaybackEmbed).IsChecked = true;
        AutoExpandBox.IsChecked = _settings.AutoExpandFeed;
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled(StartupRegistration.DefaultValueName);
        _loading = false;
        Refresh();
    }

    private void SetTheme(AppTheme theme) => Apply(() => _settings.Theme = theme);
    private void SetDensity(FeedDensity density) => Apply(() => _settings.Density = density);

    // Toggles the app's HKCU\...\Run entry. Unlike the other settings here this
    // is NOT persisted through _settings/settings.json — the registry Run key is
    // the single source of truth, shared with the tray menu's identical item.
    private void SetStartWithWindows(bool enabled)
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        if (enabled)
            StartupRegistration.Enable(StartupRegistration.DefaultValueName, exePath);
        else
            StartupRegistration.Disable(StartupRegistration.DefaultValueName);
    }

    // The Checked handlers also fire while LoadFromSettings() ticks the initial
    // radio, which would persist a no-op write on every panel construction.
    private void Apply(Action change)
    {
        if (_loading) return;
        change();
    }
}
