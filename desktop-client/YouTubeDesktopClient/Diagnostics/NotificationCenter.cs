// Diagnostics/NotificationCenter.cs
using System.Windows;

namespace YouTubeDesktopClient.Diagnostics;

public sealed record AppNotification(DateTimeOffset Time, string Message);

/// <summary>
/// The single sink for user-facing problem messages — YouTube API errors (quota,
/// permission), failed background syncs, etc. The toolbar bell shows a marker
/// while there are unread items and opens a list of them. Nothing else in the UI
/// renders these strings inline any more. Static to match <c>Logger</c>; it is
/// only ever written from the UI/background threads of this one process.
/// </summary>
public static class NotificationCenter
{
    private const int MaxItems = 100;
    private static readonly object _lock = new();
    private static readonly List<AppNotification> _items = new();

    /// <summary>Raised on the UI thread whenever the list changes.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<AppNotification> Items
    {
        get { lock (_lock) return _items.AsEnumerable().Reverse().ToList(); }
    }

    public static int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    /// <summary>Records a message. Consecutive duplicates are collapsed.</summary>
    public static void Report(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            if (_items.Count > 0 && _items[^1].Message == message) return;
            _items.Add(new AppNotification(DateTimeOffset.Now, message));
            if (_items.Count > MaxItems) _items.RemoveRange(0, _items.Count - MaxItems);
        }
        Raise();
    }

    public static void Clear()
    {
        lock (_lock)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        Raise();
    }

    private static void Raise()
    {
        var app = Application.Current;
        if (app is null) { Changed?.Invoke(null, EventArgs.Empty); return; }
        app.Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke(null, EventArgs.Empty)));
    }
}
