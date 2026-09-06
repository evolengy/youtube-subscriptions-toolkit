// Storage/VideoMerge.cs
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Storage;

public static class VideoMerge
{
    /// <summary>
    /// Combines two batches of a channel's videos, de-duplicated by
    /// <see cref="VideoInfo.VideoId"/> (the entry from <paramref name="fresher"/>
    /// wins on a clash), ordered newest first.
    /// </summary>
    public static List<VideoInfo> Dedup(IEnumerable<VideoInfo>? older, IEnumerable<VideoInfo> fresher)
    {
        var byId = new Dictionary<string, VideoInfo>();
        foreach (var v in older ?? Enumerable.Empty<VideoInfo>())
            byId[v.VideoId] = v;
        foreach (var v in fresher)
            byId[v.VideoId] = v;
        return byId.Values.OrderByDescending(v => v.PublishedAt).ToList();
    }
}
