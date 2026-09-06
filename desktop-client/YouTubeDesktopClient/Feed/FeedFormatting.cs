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
}
