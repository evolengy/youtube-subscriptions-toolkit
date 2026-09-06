namespace YouTubeDesktopClient.Tabs;

/// <summary>One open video document. Feed/Home/Channels/Settings are NOT tabs
/// anymore — they are sidebar destinations handled directly by MainWindow.</summary>
public record TabInfo(string Id, string Title, string Url, string VideoId);

/// <summary>
/// The strip of open video tabs. Holds only video documents; the active tab may
/// be null when a sidebar destination (feed/home/...) is showing instead.
/// </summary>
public class TabsViewModel
{
    private int _nextTabNumber = 1;

    public List<TabInfo> Tabs { get; } = new();

    /// <summary>Id of the video tab currently shown, or null when a sidebar
    /// destination is showing.</summary>
    public string? ActiveTabId { get; set; }

    /// <summary>
    /// Opens <paramref name="videoId"/>. Reuses the active video tab in place
    /// unless <paramref name="forceNewTab"/> is set or no video tab is active
    /// (e.g. the user was on the Feed). Returns the tab that now holds the video.
    /// </summary>
    public TabInfo OpenVideo(string videoId, string? title, bool forceNewTab)
    {
        var url = $"https://www.youtube.com/watch?v={videoId}";
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Video" : title!.Trim();

        var activeTab = Tabs.FirstOrDefault(t => t.Id == ActiveTabId);
        if (!forceNewTab && activeTab is not null)
        {
            var index = Tabs.FindIndex(t => t.Id == activeTab.Id);
            Tabs[index] = activeTab with { Url = url, Title = safeTitle, VideoId = videoId };
            ActiveTabId = Tabs[index].Id;
            return Tabs[index];
        }

        var newTab = new TabInfo($"video{_nextTabNumber++}", safeTitle, url, videoId);
        Tabs.Add(newTab);
        ActiveTabId = newTab.Id;
        return newTab;
    }

    /// <summary>Removes a tab. If it was active, the neighbour becomes active;
    /// if none remain, ActiveTabId is null (caller falls back to a destination).</summary>
    public void CloseTab(string tabId)
    {
        var index = Tabs.FindIndex(t => t.Id == tabId);
        if (index < 0) return;
        Tabs.RemoveAt(index);
        if (ActiveTabId != tabId) return;
        ActiveTabId = Tabs.Count == 0 ? null : Tabs[System.Math.Min(index, Tabs.Count - 1)].Id;
    }

    public void CloseOthers(string keepTabId)
    {
        Tabs.RemoveAll(t => t.Id != keepTabId);
        if (Tabs.Count == 1) ActiveTabId = Tabs[0].Id;
    }

    public void CloseAll()
    {
        Tabs.Clear();
        ActiveTabId = null;
    }
}
