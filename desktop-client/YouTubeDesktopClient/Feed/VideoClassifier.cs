using System.Text.RegularExpressions;

namespace YouTubeDesktopClient.Feed;

public static class VideoClassifier
{
    private const int ShortMaxSeconds = 60;
    private static readonly Regex DurationPattern =
        new(@"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$", RegexOptions.Compiled);

    public static int ParseIsoDuration(string? iso)
    {
        var match = DurationPattern.Match(iso ?? "");
        if (!match.Success) return 0;

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return hours * 3600 + minutes * 60 + seconds;
    }

    // liveBroadcastContent is checked first because a live/upcoming stream
    // can report a near-zero or stale duration while airing. Once a stream
    // has ended (liveBroadcastContent == "none"), it falls through to the
    // ordinary duration check like any past upload.
    public static string ClassifyVideoType(string liveBroadcastContent, int durationSeconds)
    {
        if (liveBroadcastContent == "live" || liveBroadcastContent == "upcoming")
            return "live";
        return durationSeconds <= ShortMaxSeconds ? "short" : "video";
    }
}
