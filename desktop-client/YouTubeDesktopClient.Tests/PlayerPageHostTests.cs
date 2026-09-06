// PlayerPageHostTests.cs
using Xunit;
using YouTubeDesktopClient.Player;

namespace YouTubeDesktopClient.Tests;

public class PlayerPageHostTests
{
    [Fact]
    public void BuildUrl_PointsAtTheVirtualHostWithVideoAndAutoplay()
    {
        var url = PlayerPageHost.BuildUrl("dQw4w9WgXcQ");

        Assert.Equal("https://ytdesktop.local/player.html?v=dQw4w9WgXcQ&autoplay=1", url);
    }

    [Fact]
    public void BuildUrl_EscapesTheVideoId()
    {
        var url = PlayerPageHost.BuildUrl("a b&c");

        Assert.Contains("v=a%20b%26c", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void WatchUrl_IsTheCanonicalYouTubeWatchLink()
    {
        Assert.Equal("https://www.youtube.com/watch?v=abc123", PlayerPageHost.WatchUrl("abc123"));
    }

    [Theory]
    [InlineData("{\"type\":\"ready\"}", PlayerEventKind.Ready, 0)]
    [InlineData("{\"type\":\"state\",\"state\":1}", PlayerEventKind.State, 1)]
    [InlineData("{\"type\":\"state\",\"state\":0}", PlayerEventKind.State, 0)]
    [InlineData("{\"type\":\"error\",\"code\":150}", PlayerEventKind.Error, 150)]
    public void Parse_ReadsKnownMessages(string json, PlayerEventKind kind, int code)
    {
        var evt = PlayerEvent.Parse(json);

        Assert.NotNull(evt);
        Assert.Equal(kind, evt!.Value.Kind);
        Assert.Equal(code, evt.Value.Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"something-else\"}")]
    [InlineData("[1,2,3]")]
    [InlineData("\"bare string\"")]
    public void Parse_ReturnsNullForUnrecognisedPayloads(string json)
    {
        Assert.Null(PlayerEvent.Parse(json));
    }

    [Fact]
    public void Parse_EndedStateExposesIsEnded()
    {
        Assert.True(PlayerEvent.Parse("{\"type\":\"state\",\"state\":0}")!.Value.IsEnded);
        Assert.False(PlayerEvent.Parse("{\"type\":\"state\",\"state\":1}")!.Value.IsEnded);
    }

    [Theory]
    [InlineData(101, true)]   // owner blocked embeds — watch page fixes it
    [InlineData(150, true)]   // same as 101
    [InlineData(100, true)]   // removed/private — offer the page anyway
    [InlineData(5, true)]     // HTML5 failure — full page often works
    [InlineData(2, false)]    // bad video id = our bug, page fails the same way
    public void IsUnembeddable_TriggersFallbackForEveryCodeButBadParam(int code, bool expected)
    {
        Assert.Equal(expected, PlayerPageHost.IsUnembeddable(code));

        var errEvt = PlayerEvent.Parse($"{{\"type\":\"error\",\"code\":{code}}}");
        Assert.Equal(expected, errEvt!.Value.IsUnembeddable);
    }

    [Fact]
    public void IsUnembeddable_IsFalseForNonErrorEvents()
    {
        Assert.False(PlayerEvent.Parse("{\"type\":\"state\",\"state\":101}")!.Value.IsUnembeddable);
    }
}
