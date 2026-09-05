using Xunit;
using YouTubeDesktopClient.Feed;

public class VideoClassifierTests
{
    [Theory]
    [InlineData("PT4M13S", 253)]
    [InlineData("PT1H2M3S", 3723)]
    [InlineData("PT45S", 45)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseIsoDuration_ParsesCorrectly(string? iso, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, VideoClassifier.ParseIsoDuration(iso));
    }

    [Fact]
    public void ClassifyVideoType_LiveTakesPriorityOverDuration()
    {
        Assert.Equal("live", VideoClassifier.ClassifyVideoType("live", 30));
    }

    [Fact]
    public void ClassifyVideoType_UpcomingIsLive()
    {
        Assert.Equal("live", VideoClassifier.ClassifyVideoType("upcoming", 0));
    }

    [Fact]
    public void ClassifyVideoType_ShortAtOrUnderThreshold()
    {
        Assert.Equal("short", VideoClassifier.ClassifyVideoType("none", 60));
    }

    [Fact]
    public void ClassifyVideoType_VideoOverThreshold()
    {
        Assert.Equal("video", VideoClassifier.ClassifyVideoType("none", 61));
    }

    [Fact]
    public void ClassifyVideoType_EndedLiveStreamFallsBackToDuration()
    {
        // liveBroadcastContent "none" means the stream ended; classification
        // then falls through to the ordinary duration check, same as any
        // past upload.
        Assert.Equal("video", VideoClassifier.ClassifyVideoType("none", 5000));
    }
}
