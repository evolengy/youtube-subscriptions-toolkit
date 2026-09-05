// Views/ChannelManagementPanel.xaml.cs
using System.Windows.Controls;
using YouTubeDesktopClient.Channels;

namespace YouTubeDesktopClient.Views;

public partial class ChannelManagementPanel : UserControl
{
    private readonly ChannelManagementViewModel _viewModel;

    public ChannelManagementPanel(ChannelManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        Refresh();
    }

    private void Refresh()
    {
        DeadChannelsList.Items.Clear();
        foreach (var (channelId, entry) in _viewModel.GetDeadChannels())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = entry.Title, Width = 200 });
            var unsubBtn = new Button { Content = "Unsubscribe" };
            unsubBtn.Click += async (_, _) =>
            {
                await _viewModel.UnsubscribeAsync(channelId, entry.SubscriptionId!);
                Refresh();
            };
            row.Children.Add(unsubBtn);
            DeadChannelsList.Items.Add(row);
        }
    }
}
