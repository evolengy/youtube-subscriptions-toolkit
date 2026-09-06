// VideoActionsViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Player;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Tests;

file sealed class ActionsFakeApi : IYouTubeApiClient
{
    public VideoActionState State { get; set; } = new("c1", "Chan", "none", null);
    public string? LastRating { get; private set; }
    public bool SubscribeCalled { get; private set; }
    public string? UnsubscribedId { get; private set; }
    public string? PostedComment { get; private set; }
    public Exception? RateThrows { get; set; }

    public Task<VideoActionState> GetVideoActionStateAsync(string t, string v) => Task.FromResult(State);

    public Task RateVideoAsync(string t, string v, string rating)
    {
        if (RateThrows != null) throw RateThrows;
        LastRating = rating;
        return Task.CompletedTask;
    }

    public Task<string> SubscribeAsync(string t, string channelId)
    {
        SubscribeCalled = true;
        return Task.FromResult("sub-123");
    }

    public Task UnsubscribeAsync(string t, string subscriptionId)
    {
        UnsubscribedId = subscriptionId;
        return Task.CompletedTask;
    }

    public Task PostCommentAsync(string t, string videoId, string text)
    {
        PostedComment = text;
        return Task.CompletedTask;
    }

    // unused by these tests
    public Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string t) => Task.FromResult(new List<SubscriptionEntry>());
    public Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string t, List<string> ids) => Task.FromResult(new Dictionary<string, ChannelDetails>());
    public Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string t, string p, string? e, int m = 15, string? pt = null) => Task.FromResult(new PlaylistItemsResult(new(), null, false));
    public Task<List<VideoInfo>> FetchVideosDetailsAsync(string t, List<string> ids) => Task.FromResult(new List<VideoInfo>());
}

public class VideoActionsViewModelTests
{
    private static VideoActionsViewModel Make(IYouTubeApiClient api, string? token = "tok") =>
        new(api, () => Task.FromResult(token));

    [Fact]
    public async Task LoadAsync_NotSignedIn_DisablesInteractionAndExplains()
    {
        var vm = Make(new ActionsFakeApi(), token: null);

        await vm.LoadAsync("v1");

        Assert.False(vm.CanInteract);
        Assert.Contains("Sign in", vm.Status);
    }

    [Fact]
    public async Task LoadAsync_ReflectsServerState()
    {
        var api = new ActionsFakeApi { State = new("c1", "Cool Channel", "like", "sub-9") };
        var vm = Make(api);

        await vm.LoadAsync("v1");

        Assert.True(vm.CanInteract);
        Assert.Equal("Cool Channel", vm.ChannelTitle);
        Assert.True(vm.IsLiked);
        Assert.True(vm.IsSubscribed);
    }

    [Fact]
    public async Task ToggleRating_Like_ThenLikeAgain_ClearsRating()
    {
        var api = new ActionsFakeApi();
        var vm = Make(api);
        await vm.LoadAsync("v1");

        await vm.ToggleRatingAsync("like");
        Assert.Equal("like", api.LastRating);
        Assert.True(vm.IsLiked);

        await vm.ToggleRatingAsync("like");
        Assert.Equal("none", api.LastRating);
        Assert.False(vm.IsLiked);
    }

    [Fact]
    public async Task ToggleSubscribe_SubscribesThenUnsubscribes()
    {
        var api = new ActionsFakeApi();
        var vm = Make(api);
        await vm.LoadAsync("v1");

        await vm.ToggleSubscribeAsync();
        Assert.True(api.SubscribeCalled);
        Assert.True(vm.IsSubscribed);

        await vm.ToggleSubscribeAsync();
        Assert.Equal("sub-123", api.UnsubscribedId);
        Assert.False(vm.IsSubscribed);
    }

    [Fact]
    public async Task ToggleRating_ServerError_KeepsOldStateAndReportsToNotificationCenter()
    {
        NotificationCenter.Clear();
        var api = new ActionsFakeApi { RateThrows = new YouTubeApiException(System.Net.HttpStatusCode.Forbidden, "quota") };
        var vm = Make(api);
        await vm.LoadAsync("v1");

        await vm.ToggleRatingAsync("like");

        Assert.False(vm.IsLiked);
        Assert.True(NotificationCenter.Count > 0);
    }

}
