using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class ChannelManagementViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public ChannelManagementViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetDeadChannels_ReturnsOnlyChannelsMarkedDead()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Alive", null, null, "UU1", Dead: false, null, "subAlive"),
            ["UC2"] = new SubscriptionCacheEntry("Dead One", null, null, null, Dead: true, null, "sub2"),
        });
        var viewModel = new ChannelManagementViewModel(new FakeYouTubeApiClient(), _store, () => Task.FromResult<string?>("token"));

        var dead = viewModel.GetDeadChannels();

        Assert.Single(dead);
        Assert.Equal("UC2", dead[0].ChannelId);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesChannelFromCache()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC2"] = new SubscriptionCacheEntry("Dead One", null, null, null, Dead: true, null, "sub2"),
        });
        var viewModel = new ChannelManagementViewModel(new FakeYouTubeApiClient(), _store, () => Task.FromResult<string?>("token"));

        await viewModel.UnsubscribeAsync("UC2", "sub2");

        Assert.False(_store.GetSubscriptionsCache().ContainsKey("UC2"));
    }
}
