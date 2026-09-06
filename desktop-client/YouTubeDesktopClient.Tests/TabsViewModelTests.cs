using Xunit;
using YouTubeDesktopClient.Tabs;

public class TabsViewModelTests
{
    [Fact]
    public void InitialState_HasNoTabs()
    {
        var vm = new TabsViewModel();

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTabId);
    }

    [Fact]
    public void OpenVideo_FromDestination_OpensNewTab()
    {
        var vm = new TabsViewModel();

        vm.OpenVideo("v1", "First video", forceNewTab: false);

        Assert.Single(vm.Tabs);
        Assert.Equal(vm.ActiveTabId, vm.Tabs[^1].Id);
        Assert.Contains("v1", vm.Tabs[^1].Url);
        Assert.Equal("First video", vm.Tabs[^1].Title);
    }

    [Fact]
    public void OpenVideo_BlankTitle_FallsBackToPlaceholder()
    {
        var vm = new TabsViewModel();

        vm.OpenVideo("v1", "   ", forceNewTab: false);

        Assert.Equal("Video", vm.Tabs[^1].Title);
    }

    [Fact]
    public void OpenVideo_FromVideoTab_ReplacesInPlace_WhenNotForced()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);
        var videoTabId = vm.ActiveTabId;

        vm.OpenVideo("v2", "v2", forceNewTab: false);

        Assert.Single(vm.Tabs); // no new tab added
        Assert.Equal(videoTabId, vm.ActiveTabId);
        Assert.Contains("v2", vm.Tabs.Single(t => t.Id == videoTabId).Url);
    }

    [Fact]
    public void OpenVideo_FromVideoTab_OpensNewTab_WhenForced()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);

        vm.OpenVideo("v2", "v2", forceNewTab: true);

        Assert.Equal(2, vm.Tabs.Count);
    }

    [Fact]
    public void CloseTab_RemovesTab_AndActivatesNeighbour()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);
        vm.OpenVideo("v2", "v2", forceNewTab: true);
        var second = vm.ActiveTabId!;

        vm.CloseTab(second);

        Assert.DoesNotContain(vm.Tabs, t => t.Id == second);
        Assert.Equal(vm.Tabs[^1].Id, vm.ActiveTabId);
    }

    [Fact]
    public void CloseTab_LastTab_LeavesActiveNull()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);
        var only = vm.ActiveTabId!;

        vm.CloseTab(only);

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTabId);
    }

    [Fact]
    public void CloseOthers_KeepsOnlyTheNamedTab()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);
        vm.OpenVideo("v2", "v2", forceNewTab: true);
        var keep = vm.Tabs[0].Id;

        vm.CloseOthers(keep);

        Assert.Single(vm.Tabs);
        Assert.Equal(keep, vm.Tabs[0].Id);
        Assert.Equal(keep, vm.ActiveTabId);
    }

    [Fact]
    public void CloseAll_EmptiesTheStrip()
    {
        var vm = new TabsViewModel();
        vm.OpenVideo("v1", "v1", forceNewTab: false);
        vm.OpenVideo("v2", "v2", forceNewTab: true);

        vm.CloseAll();

        Assert.Empty(vm.Tabs);
        Assert.Null(vm.ActiveTabId);
    }
}
