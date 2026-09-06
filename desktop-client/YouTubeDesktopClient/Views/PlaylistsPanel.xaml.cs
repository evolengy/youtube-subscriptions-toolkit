// Views/PlaylistsPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Playlists;
using YouTubeDesktopClient.Settings;

namespace YouTubeDesktopClient.Views;

/// <summary>
/// Left: the user's playlists with a create box + a Rename/Delete context menu.
/// Right: the selected playlist's videos in the shared <see cref="VideoCardGrid"/>
/// (Play / Remove per card, Play all in the header). Imperative, like GroupsPanel.
/// </summary>
public partial class PlaylistsPanel : UserControl
{
    private readonly PlaylistsViewModel _vm;
    private readonly AppSettingsViewModel _settings;
    private readonly Action<string, string> _onPlay;
    private string? _selectedId;
    private List<PlaylistItemCardViewModel> _currentItems = new();
    private bool _suppressSelection;

    public PlaylistsPanel(PlaylistsViewModel vm, AppSettingsViewModel settings, Action<string, string> onPlay)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _onPlay = onPlay;
        Cards.SetDensity(_settings.Density);
        _settings.DensityChanged += () => Cards.SetDensity(_settings.Density);
        _ = ReloadAsync();
    }

    /// <summary>Re-fetches the playlist list — called when the panel is shown.</summary>
    public async Task ReloadAsync()
    {
        await _vm.EnsureLoadedAsync(force: true);
        RefreshPlaylistList();
    }

    private void RefreshPlaylistList()
    {
        _suppressSelection = true;
        PlaylistList.Items.Clear();
        foreach (var p in _vm.Playlists)
        {
            var item = new ListBoxItem
            {
                Content = $"{p.Title}  ({p.ItemCount})",
                Tag = p.Id,
                Padding = new Thickness(8, 5, 8, 5),
                ContextMenu = BuildRowMenu(p.Id),
            };
            PlaylistList.Items.Add(item);
            if (p.Id == _selectedId) PlaylistList.SelectedItem = item;
        }
        _suppressSelection = false;

        if (_selectedId != null && _vm.Playlists.All(p => p.Id != _selectedId))
            ShowPlaylist(null);
    }

    private ContextMenu BuildRowMenu(string playlistId)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += async (_, _) => await RenameAsync(playlistId);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += async (_, _) => await DeleteAsync(playlistId);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        return menu;
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (PlaylistList.SelectedItem is ListBoxItem { Tag: string id })
            _ = ShowPlaylistAsync(id);
    }

    private async Task ShowPlaylistAsync(string playlistId)
    {
        _selectedId = playlistId;
        var playlist = _vm.Playlists.FirstOrDefault(p => p.Id == playlistId);
        HeaderText.Text = playlist?.Title ?? "";
        EmptyText.Text = "Loading…";
        EmptyText.Visibility = Visibility.Visible;
        Cards.ItemsSource = null;
        PlayAllButton.Visibility = Visibility.Collapsed;

        var entries = await _vm.GetItemsAsync(playlistId);
        if (_selectedId != playlistId) return; // selection changed while loading

        _currentItems = entries
            .Select(e => new PlaylistItemCardViewModel(e, _onPlay,
                onRemove: (itemId, _unused) => _ = RemoveItemAsync(playlistId, itemId)))
            .ToList();
        Cards.ItemsSource = _currentItems;
        EmptyText.Text = _currentItems.Count == 0 ? "This playlist is empty." : "";
        EmptyText.Visibility = _currentItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayAllButton.Visibility = _currentItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPlaylist(string? id)
    {
        if (id == null)
        {
            _selectedId = null;
            HeaderText.Text = "";
            EmptyText.Text = "Select a playlist.";
            EmptyText.Visibility = Visibility.Visible;
            Cards.ItemsSource = null;
            PlayAllButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void AddPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text;
        if (await _vm.CreateAsync(name))
        {
            NewNameBox.Clear();
            RefreshPlaylistList();
        }
    }

    private async Task RenameAsync(string playlistId)
    {
        // Reuse the create box as the rename input when a row is right-clicked.
        var name = NewNameBox.Text;
        if (string.IsNullOrWhiteSpace(name)) { NotifyNeedName(); return; }
        if (await _vm.RenameAsync(playlistId, name))
        {
            NewNameBox.Clear();
            RefreshPlaylistList();
            if (_selectedId == playlistId) await ShowPlaylistAsync(playlistId);
        }
    }

    private async Task DeleteAsync(string playlistId)
    {
        if (await _vm.DeleteAsync(playlistId))
        {
            if (_selectedId == playlistId) ShowPlaylist(null);
            RefreshPlaylistList();
        }
    }

    private async Task RemoveItemAsync(string playlistId, string playlistItemId)
    {
        if (!await _vm.RemoveItemAsync(playlistId, playlistItemId)) return;

        // playlistItems.list is eventually-consistent after a delete, so drop the
        // card locally rather than re-fetching a stale list.
        if (_selectedId == playlistId &&
            _currentItems.FirstOrDefault(i => i.PlaylistItemId == playlistItemId) is { } card)
        {
            _currentItems.Remove(card);
            Cards.ItemsSource = null;
            Cards.ItemsSource = _currentItems;
            EmptyText.Text = _currentItems.Count == 0 ? "This playlist is empty." : "";
            EmptyText.Visibility = _currentItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshPlaylistList();
    }

    private void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        var first = _currentItems.FirstOrDefault(i => i.VideoId.Length > 0);
        if (first != null) _onPlay(first.VideoId, first.Title);
    }

    private static void NotifyNeedName() =>
        Diagnostics.NotificationCenter.Report("Type the new name in the box first, then use the row's Rename.");
}
