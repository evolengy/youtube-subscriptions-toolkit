namespace YouTubeDesktopClient.Tabs;

public record TabInfo(string Id, string Title, string Url, bool IsPinned);

public class TabsViewModel
{
    private int _nextTabNumber = 1;
    public List<TabInfo> Tabs { get; } = new()
    {
        new TabInfo("feed", "Feed", "app://feed", IsPinned: true),
        new TabInfo("home", "Home", "https://www.youtube.com/", IsPinned: true),
    };

    public string ActiveTabId { get; set; } = "feed";

    public void NavigateActiveOrNewTab(string videoId, bool forceNewTab)
    {
        var url = $"https://www.youtube.com/watch?v={videoId}";
        var activeTab = Tabs.FirstOrDefault(t => t.Id == ActiveTabId);
        var activeIsReusableVideoTab = activeTab is { IsPinned: false };

        if (!forceNewTab && activeIsReusableVideoTab)
        {
            var index = Tabs.FindIndex(t => t.Id == ActiveTabId);
            Tabs[index] = activeTab! with { Url = url, Title = videoId };
            return;
        }

        var newTab = new TabInfo($"video{_nextTabNumber++}", videoId, url, IsPinned: false);
        Tabs.Add(newTab);
        ActiveTabId = newTab.Id;
    }

    public void CloseTab(string tabId)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null || tab.IsPinned) return;
        Tabs.Remove(tab);
        if (ActiveTabId == tabId) ActiveTabId = "feed";
    }
}
