// Views/ChannelManagementPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Logging;

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

    /// <summary>
    /// Re-renders the dead-channel list from the store. Public so a completed
    /// background sync can push fresh data onto the screen (see App's
    /// SyncCompleted handler). Must be called on the UI thread.
    /// </summary>
    public void Refresh()
    {
        DeadChannelsList.Items.Clear();
        foreach (var (channelId, entry) in _viewModel.GetDeadChannels())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = entry.Title, Width = 200 });
            var unsubBtn = new Button { Content = "Unsubscribe" };
            unsubBtn.Click += async (_, _) =>
            {
                // This is an async void event handler: an exception escaping it
                // (not signed in, 403/404 from the API) would crash the app
                // rather than surface as a failure the user can act on.
                try
                {
                    await _viewModel.UnsubscribeAsync(channelId, entry.SubscriptionId!);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Unsubscribe failed for channel {channelId}", ex);
                    MessageBox.Show($"Couldn't unsubscribe: {ex.Message}");
                    return;
                }
                Refresh();
            };
            row.Children.Add(unsubBtn);
            DeadChannelsList.Items.Add(row);
        }
    }
}
