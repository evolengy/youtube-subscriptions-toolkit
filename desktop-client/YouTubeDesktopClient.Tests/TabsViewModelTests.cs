using Xunit;
using YouTubeDesktopClient.Tabs;

public class TabsViewModelTests
{
    [Fact]
    public void InitialState_HasPinnedFeedAndHomeTabs()
    {
        var vm = new TabsViewModel();

        Assert.Equal(2, vm.Tabs.Count);
        Assert.All(vm.Tabs, t => Assert.True(t.IsPinned));
        Assert.Contains(vm.Tabs, t => t.Id == "feed");
        Assert.Contains(vm.Tabs, t => t.Id == "home");
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromFeedTab_AlwaysOpensNewTab()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };

        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);

        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal(vm.ActiveTabId, vm.Tabs[^1].Id);
        Assert.Contains("v1", vm.Tabs[^1].Url);
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromVideoTab_ReplacesInPlace_WhenNotForced()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);
        var videoTabId = vm.ActiveTabId;

        vm.NavigateActiveOrNewTab("v2", forceNewTab: false);

        Assert.Equal(3, vm.Tabs.Count); // no new tab added
        Assert.Equal(videoTabId, vm.ActiveTabId);
        Assert.Contains("v2", vm.Tabs.Single(t => t.Id == videoTabId).Url);
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromVideoTab_OpensNewTab_WhenForced()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);

        vm.NavigateActiveOrNewTab("v2", forceNewTab: true);

        Assert.Equal(4, vm.Tabs.Count);
    }

    [Fact]
    public void CloseTab_RemovesNonPinnedTab()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);
        var videoTabId = vm.ActiveTabId;

        vm.CloseTab(videoTabId);

        Assert.DoesNotContain(vm.Tabs, t => t.Id == videoTabId);
    }

    [Fact]
    public void CloseTab_IgnoresPinnedTab()
    {
        var vm = new TabsViewModel();

        vm.CloseTab("feed");

        Assert.Contains(vm.Tabs, t => t.Id == "feed");
    }
}
