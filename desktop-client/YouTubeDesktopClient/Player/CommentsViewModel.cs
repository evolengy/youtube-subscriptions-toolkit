// Player/CommentsViewModel.cs
using System.Collections.ObjectModel;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Player;

/// <summary>
/// The comments under the player. Lazy — <see cref="LoadAsync"/> runs the first
/// time the panel is revealed for a video. Threads paginate; replies load per
/// thread on demand. Posting a comment / reply hits the Data API (50 units) and,
/// on success, prepends a local copy — <c>commentThreads.list</c> lags a write by
/// minutes. Every failure goes to <see cref="NotificationCenter"/>.
/// </summary>
public sealed class CommentsViewModel
{
    private readonly IYouTubeCommentApi _api;
    private readonly Func<Task<string?>> _getToken;
    private readonly Func<string> _selfName;

    private string? _videoId;
    private string? _nextPageToken;

    public CommentsViewModel(IYouTubeCommentApi api, Func<Task<string?>> getToken, Func<string>? selfName = null)
    {
        _api = api;
        _getToken = getToken;
        _selfName = selfName ?? (() => "You");
    }

    public ObservableCollection<CommentThread> Threads { get; } = new();
    public bool CanComment { get; private set; }
    public bool LoadFailed { get; private set; }
    public bool HasMore => _nextPageToken != null;

    /// <summary>Raised after the thread list or a thread's replies change.</summary>
    public event Action? Changed;

    public async Task LoadAsync(string videoId)
    {
        if (_videoId == videoId && Threads.Count > 0) return; // already loaded for this video

        _videoId = videoId;
        _nextPageToken = null;
        LoadFailed = false;
        Threads.Clear();
        Changed?.Invoke();

        var token = await _getToken();
        CanComment = token != null;
        if (token is null) { Changed?.Invoke(); return; }

        try
        {
            var page = await _api.ListCommentThreadsAsync(token, videoId);
            foreach (var t in page.Threads) Threads.Add(t);
            _nextPageToken = page.NextPageToken;
        }
        catch (Exception ex) { LoadFailed = true; Report("Loading comments", ex); }
        Changed?.Invoke();
    }

    public async Task LoadMoreAsync()
    {
        if (_videoId is not { } id || _nextPageToken is not { } token0) return;
        var token = await _getToken();
        if (token is null) return;
        try
        {
            var page = await _api.ListCommentThreadsAsync(token, id, token0);
            foreach (var t in page.Threads) Threads.Add(t);
            _nextPageToken = page.NextPageToken;
        }
        catch (Exception ex) { Report("Loading more comments", ex); }
        Changed?.Invoke();
    }

    /// <summary>Full reply list for a thread (the inline preview is only ~5).</summary>
    public async Task<IReadOnlyList<CommentInfo>> LoadRepliesAsync(string parentId)
    {
        var token = await _getToken();
        if (token is null) return Array.Empty<CommentInfo>();
        try
        {
            var all = new List<CommentInfo>();
            string? page = null;
            do
            {
                var result = await _api.ListRepliesAsync(token, parentId, page);
                all.AddRange(result.Replies);
                page = result.NextPageToken;
            } while (page != null);
            return all;
        }
        catch (Exception ex) { Report("Loading replies", ex); return Array.Empty<CommentInfo>(); }
    }

    public async Task<bool> PostAsync(string text)
    {
        if (_videoId is not { } id || string.IsNullOrWhiteSpace(text)) return false;
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            await _api.PostCommentAsync(token, id, text.Trim());
            Threads.Insert(0, new CommentThread(
                "local-" + Guid.NewGuid().ToString("N"),
                SelfComment(text.Trim()), 0, Array.Empty<CommentInfo>()));
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) { Report("Posting the comment", ex); return false; }
    }

    public async Task<bool> ReplyAsync(string parentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var token = await _getToken();
        if (token is null) return false;
        try
        {
            await _api.ReplyToCommentAsync(token, parentId, text.Trim());
            return true;
        }
        catch (Exception ex) { Report("Posting the reply", ex); return false; }
    }

    private CommentInfo SelfComment(string text) =>
        new("local", _selfName(), null, text, 0, DateTimeOffset.UtcNow);

    private static void Report(string what, Exception ex)
    {
        Logger.LogError($"{what} failed", ex);
        NotificationCenter.Report($"{what} failed — {ApiErrorText.Describe(ex)}");
    }
}
