using Xunit;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Storage;

public class GroupsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GroupsViewModel _viewModel;

    public GroupsViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        var store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
        _viewModel = new GroupsViewModel(store);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void CreateGroup_AddsToGroupsList()
    {
        var id = _viewModel.CreateGroup("Music");

        Assert.Equal("Music", _viewModel.GetGroups()[id].Name);
        Assert.Empty(_viewModel.GetGroups()[id].ChannelIds);
    }

    [Fact]
    public void RenameGroup_UpdatesName()
    {
        var id = _viewModel.CreateGroup("Music");
        _viewModel.RenameGroup(id, "Podcasts");

        Assert.Equal("Podcasts", _viewModel.GetGroups()[id].Name);
    }

    [Fact]
    public void DeleteGroup_RemovesFromGroupsList()
    {
        var id = _viewModel.CreateGroup("Music");
        _viewModel.DeleteGroup(id);

        Assert.Empty(_viewModel.GetGroups());
    }

    [Fact]
    public void SetChannelInGroup_AddsThenRemovesChannel()
    {
        var id = _viewModel.CreateGroup("Music");

        _viewModel.SetChannelInGroup(id, "UC1", included: true);
        Assert.Contains("UC1", _viewModel.GetGroups()[id].ChannelIds);

        _viewModel.SetChannelInGroup(id, "UC1", included: false);
        Assert.DoesNotContain("UC1", _viewModel.GetGroups()[id].ChannelIds);
    }
}
