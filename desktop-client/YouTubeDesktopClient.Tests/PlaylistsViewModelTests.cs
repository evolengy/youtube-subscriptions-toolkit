// PlaylistsViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Playlists;

namespace YouTubeDesktopClient.Tests;

file sealed class FakePlaylistApi : IYouTubePlaylistApi
{
    public List<PlaylistSummary> Lists { get; set; } = new();
    public List<PlaylistItemEntry> Items { get; set; } = new();
    public string? LastAddedVideo { get; private set; }
    public string? LastRemovedItem { get; private set; }
    public Exception? Throws { get; set; }
    public int NextId = 1;

    public Task<List<PlaylistSummary>> ListMyPlaylistsAsync(string t) => Task.FromResult(Lists);

    public Task<string> CreatePlaylistAsync(string t, string title, string? d, string privacy = "private")
    {
        if (Throws != null) throw Throws;
        return Task.FromResult($"PL{NextId++}");
    }

    public Task UpdatePlaylistAsync(string t, string id, string title, string? d)
    {
        if (Throws != null) throw Throws;
        return Task.CompletedTask;
    }

    public Task DeletePlaylistAsync(string t, string id)
    {
        if (Throws != null) throw Throws;
        return Task.CompletedTask;
    }

    public Task<List<PlaylistItemEntry>> ListPlaylistItemsAsync(string t, string id) => Task.FromResult(Items);

    public Task<string> AddToPlaylistAsync(string t, string id, string videoId)
    {
        if (Throws != null) throw Throws;
        LastAddedVideo = videoId;
        return Task.FromResult("pliNew");
    }

    public Task RemoveFromPlaylistAsync(string t, string playlistItemId)
    {
        if (Throws != null) throw Throws;
        LastRemovedItem = playlistItemId;
        return Task.CompletedTask;
    }
}

public class PlaylistsViewModelTests
{
    private static PlaylistsViewModel Make(IYouTubePlaylistApi api) =>
        new(api, () => Task.FromResult<string?>("token"));

    [Fact]
    public async Task EnsureLoadedAsync_FetchesOnce()
    {
        var api = new FakePlaylistApi { Lists = new() { new("PL1", "One", "", 3, null, "private") } };
        var vm = Make(api);

        await vm.EnsureLoadedAsync();
        await vm.EnsureLoadedAsync(); // second call is a no-op

        Assert.Single(vm.Playlists);
        Assert.Equal("One", vm.Playlists[0].Title);
    }

    [Fact]
    public async Task CreateAsync_PrependsTheNewPlaylist()
    {
        var api = new FakePlaylistApi();
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.True(await vm.CreateAsync("Watch later"));

        Assert.Equal("Watch later", vm.Playlists[0].Title);
        Assert.Equal("PL1", vm.Playlists[0].Id);
    }

    [Fact]
    public async Task RenameAsync_UpdatesTheTitleInPlace()
    {
        var api = new FakePlaylistApi { Lists = new() { new("PL1", "Old", "", 0, null, "private") } };
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.True(await vm.RenameAsync("PL1", "New"));
        Assert.Equal("New", vm.Playlists[0].Title);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromTheList()
    {
        var api = new FakePlaylistApi { Lists = new() { new("PL1", "Gone", "", 0, null, "private") } };
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.True(await vm.DeleteAsync("PL1"));
        Assert.Empty(vm.Playlists);
    }

    [Fact]
    public async Task AddVideoAsync_CallsApiAndBumpsItemCount()
    {
        var api = new FakePlaylistApi { Lists = new() { new("PL1", "L", "", 2, null, "private") } };
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.True(await vm.AddVideoAsync("PL1", "vid9"));
        Assert.Equal("vid9", api.LastAddedVideo);
        Assert.Equal(3, vm.Playlists[0].ItemCount);
    }

    [Fact]
    public async Task RemoveItemAsync_CallsApiWithItemIdAndDecrementsCount()
    {
        var api = new FakePlaylistApi { Lists = new() { new("PL1", "L", "", 2, null, "private") } };
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.True(await vm.RemoveItemAsync("PL1", "pli7"));
        Assert.Equal("pli7", api.LastRemovedItem);
        Assert.Equal(1, vm.Playlists[0].ItemCount);
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReportsAndAddsNothing()
    {
        NotificationCenter.Clear();
        var api = new FakePlaylistApi { Throws = new YouTubeApiException(System.Net.HttpStatusCode.Forbidden, "quotaExceeded") };
        var vm = Make(api);
        await vm.EnsureLoadedAsync();

        Assert.False(await vm.CreateAsync("nope"));
        Assert.Empty(vm.Playlists);
        Assert.True(NotificationCenter.Count > 0);
    }

    [Fact]
    public async Task CreateAsync_BlankTitle_DoesNothing()
    {
        var api = new FakePlaylistApi();
        var vm = Make(api);

        Assert.False(await vm.CreateAsync("   "));
        Assert.Empty(vm.Playlists);
    }
}
