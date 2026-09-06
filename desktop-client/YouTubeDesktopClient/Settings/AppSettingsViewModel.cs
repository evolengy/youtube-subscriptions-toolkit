// Settings/AppSettingsViewModel.cs
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;
using YouTubeDesktopClient.Themes;

namespace YouTubeDesktopClient.Settings;

/// <summary>
/// Single source of truth for user preferences at runtime. Wraps the JSON store
/// and the ThemeManager: every setter persists immediately and pushes the change
/// where it needs to go (ThemeManager for theme, an event for feed density).
/// </summary>
public class AppSettingsViewModel
{
    private readonly SubscriptionStore _store;
    private AppSettings _current;

    /// <summary>Raised after the density preference changes so the feed can re-lay-out.</summary>
    public event Action? DensityChanged;

    public AppSettingsViewModel(SubscriptionStore store)
    {
        _store = store;
        _current = store.GetAppSettings();
    }

    public AppTheme Theme
    {
        get => ThemeManager.Parse(_current.Theme);
        set
        {
            _current = _current with { Theme = value.ToString() };
            _store.SaveAppSettings(_current);
            ThemeManager.Apply(value);
        }
    }

    public bool AutoExpandFeed
    {
        get => _current.AutoExpandFeed;
        set
        {
            _current = _current with { AutoExpandFeed = value };
            _store.SaveAppSettings(_current);
        }
    }

    public FeedDensity Density
    {
        get => Enum.TryParse<FeedDensity>(_current.Density, out var d) ? d : FeedDensity.Comfortable;
        set
        {
            _current = _current with { Density = value.ToString() };
            _store.SaveAppSettings(_current);
            DensityChanged?.Invoke();
        }
    }

    /// <summary>Cycles the toolbar toggle: Light -> Dark -> System -> Light.</summary>
    public void CycleTheme() => Theme = Theme switch
    {
        AppTheme.Light => AppTheme.Dark,
        AppTheme.Dark => AppTheme.System,
        _ => AppTheme.Light,
    };
}
