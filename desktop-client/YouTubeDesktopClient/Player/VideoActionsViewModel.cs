// Player/VideoActionsViewModel.cs
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Diagnostics;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Player;

/// <summary>
/// Backs the current video: the action bar above the player AND the native
/// metadata strip on the embed player page. Everything here goes through the
/// YouTube Data API on the OAuth token the app already holds (scope
/// <c>.../auth/youtube</c>) — it does NOT depend on a web session inside the
/// WebView2, so Like / Subscribe / Comment work even when the embedded page shows
/// "Sign in". Watch history is deliberately absent: no API can write it.
///
/// Calls wait for the server and only then flip local state, so the bar never
/// shows a like that didn't actually land. Buttons are disabled while a call is
/// in flight (<see cref="Busy"/>). The metadata (<see cref="Description"/>,
/// <see cref="PublishedAt"/>, <see cref="ViewCount"/>) rides along on the same
/// <c>GetVideoActionStateAsync</c> response — no extra quota.
/// </summary>
public class VideoActionsViewModel : INotifyPropertyChanged
{
    private readonly IYouTubeApiClient _api;
    private readonly Func<Task<string?>> _getToken;

    private string? _videoId;
    private string _channelId = "";
    private string _rating = "none";        // "like" | "dislike" | "none"
    private string? _subscriptionId;        // non-null => subscribed
    private bool _busy;
    private string? _status;

    public VideoActionsViewModel(IYouTubeApiClient api, Func<Task<string?>> getToken)
    {
        _api = api;
        _getToken = getToken;
    }

    public string ChannelTitle { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DateTimeOffset? PublishedAt { get; private set; }
    public long ViewCount { get; private set; }

    /// <summary>Raised once a video's metadata (title/channel/views/date/description)
    /// has loaded, so the player page can fill its native strip.</summary>
    public event Action? MetadataLoaded;

    public bool CanInteract { get; private set; }
    public bool IsLiked => _rating == "like";
    public bool IsDisliked => _rating == "dislike";
    public bool IsSubscribed => _subscriptionId != null;
    public string SubscribeLabel => IsSubscribed ? "Subscribed ✓" : "Subscribe";

    public bool Busy
    {
        get => _busy;
        private set { if (_busy == value) return; _busy = value; Raise(); }
    }

    public string? Status
    {
        get => _status;
        private set { if (_status == value) return; _status = value; Raise(); }
    }

    /// <summary>Fetches owner + current rating + subscription for a freshly shown video.</summary>
    public async Task LoadAsync(string videoId)
    {
        _videoId = videoId;
        _channelId = "";
        ChannelTitle = "";
        Description = "";
        PublishedAt = null;
        ViewCount = 0;
        _rating = "none";
        _subscriptionId = null;
        CanInteract = false;
        Status = null;
        RaiseAll();
        MetadataLoaded?.Invoke();

        var token = await _getToken();
        if (token is null)
        {
            Status = "Sign in on the Home tab to like, subscribe or comment.";
            return;
        }

        try
        {
            Busy = true;
            var state = await _api.GetVideoActionStateAsync(token, videoId);
            _channelId = state.ChannelId;
            ChannelTitle = state.ChannelTitle;
            Description = state.Description;
            PublishedAt = state.PublishedAt;
            ViewCount = state.ViewCount;
            _rating = state.Rating;
            _subscriptionId = state.SubscriptionId;
            CanInteract = true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Loading video action state for {videoId} failed", ex);
            NotificationCenter.Report(ApiErrorText.Describe(ex));
        }
        finally
        {
            Busy = false;
            RaiseAll();
            MetadataLoaded?.Invoke();
        }
    }

    /// <summary>Clicking the button you're already on clears the rating (YouTube's own behaviour).</summary>
    public Task ToggleRatingAsync(string desired) =>
        Run(async (token, id) =>
        {
            var next = _rating == desired ? "none" : desired;
            await _api.RateVideoAsync(token, id, next);
            _rating = next;
        });

    public Task ToggleSubscribeAsync() =>
        Run(async (token, _) =>
        {
            if (_subscriptionId is { } existing)
            {
                await _api.UnsubscribeAsync(token, existing);
                _subscriptionId = null;
            }
            else
            {
                _subscriptionId = await _api.SubscribeAsync(token, _channelId);
            }
        });

    private async Task Run(Func<string, string, Task> action)
    {
        if (_videoId is not { } id || !CanInteract || Busy) return;
        var token = await _getToken();
        if (token is null) { Status = "Not signed in."; return; }

        try
        {
            Busy = true;
            Status = null;
            RaiseAll();
            await action(token, id);
        }
        catch (Exception ex)
        {
            Logger.LogError("Player action failed", ex);
            NotificationCenter.Report(ApiErrorText.Describe(ex));
        }
        finally
        {
            Busy = false;
            RaiseAll();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void RaiseAll()
    {
        foreach (var p in new[]
        {
            nameof(ChannelTitle), nameof(CanInteract), nameof(IsLiked), nameof(IsDisliked),
            nameof(IsSubscribed), nameof(SubscribeLabel), nameof(Busy), nameof(Status),
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
