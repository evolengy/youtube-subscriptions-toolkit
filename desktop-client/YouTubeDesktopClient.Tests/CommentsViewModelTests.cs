// CommentsViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Player;

namespace YouTubeDesktopClient.Tests;

file sealed class FakeCommentApi : IYouTubeCommentApi
{
    public Dictionary<string, CommentPage> Pages { get; set; } = new();
    public List<CommentInfo> Replies { get; set; } = new();
    public string? LastPostedTo { get; private set; }
    public string? LastRepliedParent { get; private set; }
    public Exception? PostThrows { get; set; }

    public Task<CommentPage> ListCommentThreadsAsync(string t, string videoId, string? pageToken = null) =>
        Task.FromResult(Pages.GetValueOrDefault(pageToken ?? "first", new CommentPage(Array.Empty<CommentThread>(), null)));

    public Task<ReplyPage> ListRepliesAsync(string t, string parentId, string? pageToken = null) =>
        Task.FromResult(new ReplyPage(Replies, null));

    public Task ReplyToCommentAsync(string t, string parentId, string text)
    {
        if (PostThrows != null) throw PostThrows;
        LastRepliedParent = parentId;
        return Task.CompletedTask;
    }

    public Task PostCommentAsync(string t, string videoId, string text)
    {
        if (PostThrows != null) throw PostThrows;
        LastPostedTo = videoId;
        return Task.CompletedTask;
    }
}

public class CommentsViewModelTests
{
    private static CommentInfo C(string id, string text) =>
        new(id, "Someone", null, text, 0, System.DateTimeOffset.UtcNow);

    private static CommentsViewModel Make(IYouTubeCommentApi api, string? token = "tok") =>
        new(api, () => Task.FromResult(token));

    [Fact]
    public async Task LoadAsync_PopulatesThreadsAndPagination()
    {
        var api = new FakeCommentApi
        {
            Pages =
            {
                ["first"] = new CommentPage(new[]
                {
                    new CommentThread("th1", C("c1", "hi"), 2, new[] { C("r1", "reply") }),
                }, "page2"),
            },
        };
        var vm = Make(api);

        await vm.LoadAsync("v1");

        Assert.Single(vm.Threads);
        Assert.Equal("hi", vm.Threads[0].Top.Text);
        Assert.True(vm.HasMore);
        Assert.True(vm.CanComment);
    }

    [Fact]
    public async Task LoadAsync_SignedOut_LoadsNothingAndCannotComment()
    {
        var vm = Make(new FakeCommentApi(), token: null);

        await vm.LoadAsync("v1");

        Assert.Empty(vm.Threads);
        Assert.False(vm.CanComment);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsTheNextPage()
    {
        var api = new FakeCommentApi
        {
            Pages =
            {
                ["first"] = new CommentPage(new[] { new CommentThread("th1", C("c1", "a"), 0, System.Array.Empty<CommentInfo>()) }, "page2"),
                ["page2"] = new CommentPage(new[] { new CommentThread("th2", C("c2", "b"), 0, System.Array.Empty<CommentInfo>()) }, null),
            },
        };
        var vm = Make(api);
        await vm.LoadAsync("v1");

        await vm.LoadMoreAsync();

        Assert.Equal(2, vm.Threads.Count);
        Assert.False(vm.HasMore);
    }

    [Fact]
    public async Task LoadRepliesAsync_ReturnsTheFullList()
    {
        var api = new FakeCommentApi { Replies = { C("r1", "one"), C("r2", "two") } };
        var vm = Make(api);

        var replies = await vm.LoadRepliesAsync("th1");

        Assert.Equal(2, replies.Count);
    }

    [Fact]
    public async Task PostAsync_CallsApiAndPrependsALocalThread()
    {
        var api = new FakeCommentApi();
        var vm = Make(api);
        await vm.LoadAsync("v1");

        Assert.True(await vm.PostAsync("great video"));

        Assert.Equal("v1", api.LastPostedTo);
        Assert.Equal("great video", vm.Threads[0].Top.Text);
    }

    [Fact]
    public async Task ReplyAsync_CallsApiWithParentId()
    {
        var api = new FakeCommentApi();
        var vm = Make(api);

        Assert.True(await vm.ReplyAsync("th7", "agreed"));
        Assert.Equal("th7", api.LastRepliedParent);
    }

    [Fact]
    public async Task PostAsync_ServerError_ReportsAndAddsNothing()
    {
        NotificationCenter.Clear();
        var api = new FakeCommentApi { PostThrows = new YouTubeApiException(System.Net.HttpStatusCode.Forbidden, "quotaExceeded") };
        var vm = Make(api);
        await vm.LoadAsync("v1");

        Assert.False(await vm.PostAsync("nope"));
        Assert.Empty(vm.Threads);
        Assert.True(NotificationCenter.Count > 0);
    }

    [Fact]
    public async Task PostAsync_Blank_DoesNothing()
    {
        var api = new FakeCommentApi();
        var vm = Make(api);
        await vm.LoadAsync("v1");

        Assert.False(await vm.PostAsync("   "));
        Assert.Null(api.LastPostedTo);
    }
}
