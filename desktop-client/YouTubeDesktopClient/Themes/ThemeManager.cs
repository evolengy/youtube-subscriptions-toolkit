// Themes/ThemeManager.cs
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Themes;

public enum AppTheme { Light, Dark, System }

/// <summary>
/// Owns the live light/dark swap and the Windows system-accent brushes.
///
/// Light.xaml / Dark.xaml carry identical keys; every Style in Controls.xaml
/// reads them via DynamicResource, so replacing the merged theme dictionary
/// re-paints the whole app with no restart. Accent.* is not in either theme
/// file — it is pulled from the OS and written straight into
/// Application.Resources here, so it survives a theme swap and refreshes when
/// the user changes their Windows accent colour.
/// </summary>
public static class ThemeManager
{
    private static AppTheme _selected = AppTheme.System;

    public static AppTheme Selected => _selected;
    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    /// <summary>Call once at startup with the persisted choice.</summary>
    public static void Initialize(string savedTheme)
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply(Parse(savedTheme));
    }

    public static AppTheme Parse(string? value) => value switch
    {
        "Light" => AppTheme.Light,
        "Dark" => AppTheme.Dark,
        _ => AppTheme.System,
    };

    public static void Apply(AppTheme theme)
    {
        _selected = theme;
        ApplyResolved();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General covers both the light/dark switch and the accent colour.
        if (e.Category != UserPreferenceCategory.General) return;
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.Invoke(ApplyResolved);
    }

    private static void ApplyResolved()
    {
        var app = Application.Current;
        if (app == null) return;

        IsDark = _selected switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemInDarkMode(),
        };

        var dicts = app.Resources.MergedDictionaries;
        var themeUri = new Uri(
            IsDark ? "pack://application:,,,/Themes/Dark.xaml"
                   : "pack://application:,,,/Themes/Light.xaml");

        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? string.Empty;
            if (src.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
                dicts.RemoveAt(i);
        }
        dicts.Insert(0, new ResourceDictionary { Source = themeUri });

        ApplyAccent(app);
        ThemeChanged?.Invoke();
    }

    private static void ApplyAccent(Application app)
    {
        var accent = TryGetSystemAccent() ?? Color.FromRgb(0x00, 0x67, 0xC0);
        var hover = Mix(accent, IsDark ? Colors.White : Colors.Black, 0.12);
        var subtle = Color.FromArgb(0x28, accent.R, accent.G, accent.B);
        var fg = PerceivedLuminance(accent) > 0.55 ? Colors.Black : Colors.White;

        app.Resources["Color.Accent"] = accent;
        app.Resources["Brush.Accent"] = Frozen(accent);
        app.Resources["Brush.AccentHover"] = Frozen(hover);
        app.Resources["Brush.AccentSubtle"] = Frozen(subtle);
        app.Resources["Brush.AccentFg"] = Frozen(fg);
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex)
        {
            Logger.LogError("Could not read system theme; assuming light", ex);
            return false;
        }
    }

    private static Color? TryGetSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                // DWM stores the accent as 0xAABBGGRR.
                byte r = (byte)(abgr & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Could not read system accent colour; using fallback", ex);
        }
        return null;
    }

    private static double PerceivedLuminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
