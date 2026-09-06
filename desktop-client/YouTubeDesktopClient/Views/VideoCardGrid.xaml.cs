// Views/VideoCardGrid.xaml.cs
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Views;

/// <summary>
/// The virtualized adaptive card grid — the mechanics shared by the Feed and the
/// Playlists panels: a <see cref="ListBox"/> over a WpfToolkit
/// <c>VirtualizingWrapPanel</c>, density-driven card sizes, the two panel gotchas
/// (raised <c>MouseWheelDelta</c>, and pinning the real inner <see cref="ScrollViewer"/>
/// to no-horizontal-scroll), and a <see cref="NearEndReached"/> event for
/// scroll-to-load-more. Each consumer supplies its own <see cref="ItemTemplate"/>.
/// </summary>
public partial class VideoCardGrid : UserControl
{
    public VideoCardGrid()
    {
        InitializeComponent();
        Grid.Loaded += (_, _) => PinNoHorizontalScroll();
        Grid.SizeChanged += (_, _) => PinNoHorizontalScroll();
    }

    /// <summary>Raised when the user scrolls within two viewports of the bottom.</summary>
    public event Action? NearEndReached;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(VideoCardGrid), new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(VideoCardGrid), new PropertyMetadata(null));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public void SetDensity(FeedDensity density)
    {
        var layout = FeedLayout.For(density);
        Resources["Feed.CardWidth"] = layout.CardWidth;
        Resources["Feed.ThumbHeight"] = layout.ThumbnailHeight;
        Resources["Feed.TitleHeight"] = layout.TitleHeight;
    }

    private ScrollViewer? _scrollViewer;

    private void PinNoHorizontalScroll()
    {
        _scrollViewer ??= FindDescendant<ScrollViewer>(Grid);
        if (_scrollViewer is { } sv && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void Grid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && e.VerticalChange == 0) return; // layout pass, not a user scroll
        var distanceToEnd = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
        if (distanceToEnd <= e.ViewportHeight * 2) NearEndReached?.Invoke();
    }
}
