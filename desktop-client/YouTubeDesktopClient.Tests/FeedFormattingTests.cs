// FeedFormattingTests.cs
using System;
using Xunit;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Tests;

public class FeedFormattingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(40, "just now")]
    [InlineData(90, "1 minute ago")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(3600 * 5, "5 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(86400 * 3, "3 days ago")]
    [InlineData(86400 * 10, "1 week ago")]
    [InlineData(86400 * 21, "3 weeks ago")]
    [InlineData(86400 * 75, "2 months ago")]
    [InlineData(86400 * 400, "1 year ago")]
    [InlineData(86400 * 900, "2 years ago")]
    public void RelativeDate_BucketsElapsedTime(int secondsAgo, string expected)
    {
        var when = Now.AddSeconds(-secondsAgo);
        Assert.Equal(expected, FeedFormatting.RelativeDate(when, Now));
    }

    [Fact]
    public void RelativeDate_FutureTimestamp_ReadsAsJustNow()
    {
        Assert.Equal("just now", FeedFormatting.RelativeDate(Now.AddMinutes(5), Now));
    }
}
