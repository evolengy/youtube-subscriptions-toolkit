using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Groups;

public class GroupsViewModel
{
    private readonly SubscriptionStore _store;

    public GroupsViewModel(SubscriptionStore store) => _store = store;

    public Dictionary<string, GroupData> GetGroups() => _store.GetGroups();

    /// <summary>
    /// Every known subscribed channel, keyed by channel id — what the panel
    /// offers as group membership candidates. Routed through the view model so
    /// the panel doesn't need a second dependency on the store.
    /// </summary>
    public Dictionary<string, SubscriptionCacheEntry> GetAllChannels() => _store.GetSubscriptionsCache();

    public string CreateGroup(string name)
    {
        var groups = _store.GetGroups();
        var id = $"g_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        groups[id] = new GroupData(name, new List<string>());
        _store.SaveGroups(groups);
        return id;
    }

    public void RenameGroup(string groupId, string newName)
    {
        var groups = _store.GetGroups();
        groups[groupId] = groups[groupId] with { Name = newName };
        _store.SaveGroups(groups);
    }

    public void DeleteGroup(string groupId)
    {
        var groups = _store.GetGroups();
        groups.Remove(groupId);
        _store.SaveGroups(groups);
    }

    public void SetChannelInGroup(string groupId, string channelId, bool included)
    {
        var groups = _store.GetGroups();
        var group = groups[groupId];
        var channelIds = new List<string>(group.ChannelIds);
        if (included && !channelIds.Contains(channelId)) channelIds.Add(channelId);
        if (!included) channelIds.Remove(channelId);
        groups[groupId] = group with { ChannelIds = channelIds };
        _store.SaveGroups(groups);
    }
}
