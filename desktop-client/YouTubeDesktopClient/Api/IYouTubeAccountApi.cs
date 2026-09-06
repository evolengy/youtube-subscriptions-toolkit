namespace YouTubeDesktopClient.Api;

/// <summary>
/// Account-level reads. Split off <see cref="IYouTubeApiClient"/> so the account
/// view model (and its test fake) doesn't carry the whole sync surface.
/// <see cref="YouTubeApiClient"/> implements every I*Api interface.
/// </summary>
public interface IYouTubeAccountApi
{
    /// <summary>The signed-in user's own channel. Null if the account has no
    /// channel (a bare Google account that never created one).</summary>
    Task<MyChannel?> GetMyChannelAsync(string accessToken);
}
