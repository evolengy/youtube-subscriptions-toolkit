// Feed/FeedDensity.cs
namespace YouTubeDesktopClient.Feed;

/// <summary>
/// Feed grid density. Not a theme — a second set of *sizing* tokens layered on
/// the same colour system. Drives VirtualizingWrapPanel.ItemSize and the card
/// template's line count. Persisted as a string in AppSettings.Density.
/// </summary>
public enum FeedDensity
{
    Comfortable,
    Compact,
}
