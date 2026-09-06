// Feed/FeedLayout.cs
namespace YouTubeDesktopClient.Feed;

/// <summary>
/// Card + grid dimensions for a feed density. Pure data: the panel reads these
/// to size the VirtualizingWrapPanel items and the card template. Same colour
/// system for both densities — only the measurements change.
/// </summary>
/// <param name="CardWidth">Card width in px. Drives how many columns the wrap panel fits.</param>
/// <param name="ThumbnailHeight">Thumbnail height in px. ≈ CardWidth * 9/16 for a 16:9 frame.</param>
/// <param name="TitleHeight">Title block height in px. ≈ 19 per line at the 13px title font.</param>
public record FeedLayout(double CardWidth, double ThumbnailHeight, double TitleHeight)
{
    // Content area is ~1000px wide (1280 window − 260 sidebar − chrome), and each
    // card adds 12px of margin: Comfortable fits ~3 columns, Compact ~5.
    public static FeedLayout For(FeedDensity density) => density switch
    {
        FeedDensity.Compact => new FeedLayout(CardWidth: 180, ThumbnailHeight: 101, TitleHeight: 19),
        _ => new FeedLayout(CardWidth: 300, ThumbnailHeight: 169, TitleHeight: 38),
    };
}
