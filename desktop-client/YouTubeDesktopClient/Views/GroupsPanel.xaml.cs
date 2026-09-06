// Views/GroupsPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YouTubeDesktopClient.Groups;

namespace YouTubeDesktopClient.Views;

public partial class GroupsPanel : UserControl
{
    private readonly GroupsViewModel _viewModel;
    private readonly Action<string?> _onGroupSelected;
    private string? _selectedGroupId;

    // Rebuilding GroupList clears and re-adds items, which raises
    // SelectionChanged spuriously; without this the panel would report
    // "no group selected" to the feed every time a group is edited.
    private bool _suppressSelectionEvents;

    public GroupsPanel(GroupsViewModel viewModel, Action<string?> onGroupSelected)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onGroupSelected = onGroupSelected;
        RefreshGroupList();
        RefreshChannelList();
    }

    private void RefreshGroupList()
    {
        _suppressSelectionEvents = true;
        try
        {
            GroupList.Items.Clear();
            GroupList.Items.Add(new ListBoxItem { Content = "All subscriptions", Padding = new Thickness(8, 6, 8, 6) });

            foreach (var (groupId, group) in _viewModel.GetGroups())
            {
                var row = new DockPanel { LastChildFill = true };

                // Trash glyph (Segoe Fluent Icons E74D), borderless, right-aligned.
                var deleteButton = new Button
                {
                    Content = "",
                    FontFamily = (FontFamily)FindResource("Font.Icon"),
                    Style = (Style)FindResource("Button.Icon"),
                    FontSize = 13,
                    Width = 26,
                    Height = 24,
                    ToolTip = "Delete group",
                };
                deleteButton.Click += (_, _) => DeleteGroup(groupId);
                DockPanel.SetDock(deleteButton, Dock.Right);
                row.Children.Add(deleteButton);

                row.Children.Add(new TextBlock
                {
                    Text = $"{group.Name}  ({group.ChannelIds.Count})",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var item = new ListBoxItem { Content = row, Tag = groupId, Padding = new Thickness(8, 4, 4, 4) };
                GroupList.Items.Add(item);
                if (groupId == _selectedGroupId) GroupList.SelectedItem = item;
            }

            if (_selectedGroupId == null) GroupList.SelectedIndex = 0;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RenameGroupButton.IsEnabled = _selectedGroupId != null;
    }

    private void RefreshChannelList()
    {
        ChannelList.Items.Clear();

        if (_selectedGroupId == null ||
            !_viewModel.GetGroups().TryGetValue(_selectedGroupId, out var group))
        {
            ChannelsHeader.Visibility = Visibility.Collapsed;
            ChannelList.Visibility = Visibility.Collapsed;
            return;
        }

        ChannelsHeader.Visibility = Visibility.Visible;
        ChannelList.Visibility = Visibility.Visible;

        var members = new HashSet<string>(group.ChannelIds);
        var channels = _viewModel.GetAllChannels()
            .Where(kv => !kv.Value.Dead)
            .OrderBy(kv => kv.Value.Title, StringComparer.CurrentCultureIgnoreCase);

        foreach (var (channelId, entry) in channels)
        {
            var checkBox = new CheckBox
            {
                Content = entry.Title,
                IsChecked = members.Contains(channelId),
            };
            checkBox.Checked += (_, _) => SetChannelInGroup(channelId, included: true);
            checkBox.Unchecked += (_, _) => SetChannelInGroup(channelId, included: false);
            ChannelList.Items.Add(checkBox);
        }
    }

    private void SetChannelInGroup(string channelId, bool included)
    {
        if (_selectedGroupId == null) return;
        _viewModel.SetChannelInGroup(_selectedGroupId, channelId, included);
        // Only the group list is rebuilt (its channel count changed); the
        // checkbox list is deliberately left alone, since rebuilding it from
        // inside one of its own checkbox handlers is needless churn.
        RefreshGroupList();
        _onGroupSelected(_selectedGroupId);
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents) return;
        _selectedGroupId = GroupList.SelectedItem is ListBoxItem { Tag: string id } ? id : null;
        RenameGroupButton.IsEnabled = _selectedGroupId != null;
        _onGroupSelected(_selectedGroupId);
        RefreshChannelList();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewGroupName.Text)) return;
        _viewModel.CreateGroup(NewGroupName.Text);
        NewGroupName.Clear();
        RefreshGroupList();
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroupId == null || string.IsNullOrWhiteSpace(NewGroupName.Text)) return;
        _viewModel.RenameGroup(_selectedGroupId, NewGroupName.Text);
        NewGroupName.Clear();
        RefreshGroupList();
    }

    private void DeleteGroup(string groupId)
    {
        _viewModel.DeleteGroup(groupId);
        if (_selectedGroupId == groupId)
        {
            _selectedGroupId = null;
            _onGroupSelected(null);
        }
        RefreshGroupList();
        RefreshChannelList();
    }
}
