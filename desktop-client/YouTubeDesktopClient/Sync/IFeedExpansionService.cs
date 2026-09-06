// Sync/IFeedExpansionService.cs
namespace YouTubeDesktopClient.Sync;

/// <summary>
/// Pulls older uploads history from YouTube on demand, when the feed has shown
/// everything in the local cache and the user keeps scrolling.
/// </summary>
public interface IFeedExpansionService
{
    /// <summary>
    /// True once the API has reported the daily quota is spent. Auto-expansion
    /// stops silently for the rest of the session; a manual "Refresh now" still
    /// works and the flag resets next launch.
    /// </summary>
    bool QuotaExhausted { get; }

    /// <summary>
    /// Fetches one more page of history for every channel in <paramref name="channelIds"/>
    /// that still has some, writes it to the store, and returns whether anything
    /// new was added (false = nothing left to pull, not signed in, or quota spent).
    /// </summary>
    Task<bool> ExpandAsync(IReadOnlyCollection<string> channelIds, CancellationToken ct = default);
}

/// <summary>No-op used in tests and any context without a live API client.</summary>
public sealed class NullFeedExpansionService : IFeedExpansionService
{
    public bool QuotaExhausted => false;
    public Task<bool> ExpandAsync(IReadOnlyCollection<string> channelIds, CancellationToken ct = default)
        => Task.FromResult(false);
}
