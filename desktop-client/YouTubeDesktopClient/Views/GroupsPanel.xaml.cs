// Views/GroupsPanel.xaml.cs
using System.Windows.Controls;
using YouTubeDesktopClient.Groups;

namespace YouTubeDesktopClient.Views;

public partial class GroupsPanel : UserControl
{
    private readonly GroupsViewModel _viewModel;
    private readonly Action<string?> _onGroupSelected;

    public GroupsPanel(GroupsViewModel viewModel, Action<string?> onGroupSelected)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onGroupSelected = onGroupSelected;
        RefreshList();
    }

    private void RefreshList()
    {
        GroupList.Items.Clear();
        GroupList.Items.Add("All subscriptions");
        foreach (var (id, group) in _viewModel.GetGroups())
            GroupList.Items.Add(new ListBoxItem { Content = $"{group.Name} ({group.ChannelIds.Count})", Tag = id });
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var groupId = GroupList.SelectedItem is ListBoxItem { Tag: string id } ? id : null;
        _onGroupSelected(groupId);
    }

    private void AddGroup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewGroupName.Text)) return;
        _viewModel.CreateGroup(NewGroupName.Text);
        NewGroupName.Clear();
        RefreshList();
    }
}
