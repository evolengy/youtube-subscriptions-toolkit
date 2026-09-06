// Feed/FeedFormatting.cs
namespace YouTubeDesktopClient.Feed;

/// <summary>
/// Pure string formatting for feed cards — moved out of the panel's code-behind
/// so the DataTemplate can bind a ready-made string and the logic stays testable.
/// </summary>
public static class FeedFormatting
{
    public static string MetaLine(FeedItem item, string? country)
    {
        var parts = new List<string>
        {
            item.Type,
            Duration(item.DurationSeconds),
            $"{item.Video.ViewCount:N0} views",
        };
        if (!string.IsNullOrWhiteSpace(country)) parts.Add(country);
        return string.Join("  ·  ", parts);
    }

    public static string Duration(int durationSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    /// <summary>
    /// A coarse "3 hours ago" / "2 weeks ago" label, shown under the player on
    /// the native video page. English to match the rest of the app's UI strings.
    /// A publish time in the future (clock skew) reads as "just now".
    /// </summary>
    public static string RelativeDate(DateTimeOffset when) => RelativeDate(when, DateTimeOffset.UtcNow);

    public static string RelativeDate(DateTimeOffset when, DateTimeOffset now)
    {
        var seconds = (now - when).TotalSeconds;
        if (seconds < 60) return "just now";

        (double unit, string noun) = seconds switch
        {
            < 3600 => (60, "minute"),
            < 86400 => (3600, "hour"),
            < 604800 => (86400, "day"),
            < 2629800 => (604800, "week"),      // 30.44-day month as the week ceiling
            < 31557600 => (2629800, "month"),   // 365.25-day year
            _ => (31557600, "year"),
        };

        int n = (int)(seconds / unit);
        return $"{n} {noun}{(n == 1 ? "" : "s")} ago";
    }
}
