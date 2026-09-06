namespace YouTubeDesktopClient.Auth;

/// <summary>
/// The auth surface <see cref="Account.AccountViewModel"/> needs — extracted so
/// the view model can be unit-tested without a real browser round trip.
/// </summary>
public interface IAuthService
{
    Task<string?> GetAccessTokenSilentAsync();
    Task<string> SignInInteractiveAsync(CancellationToken ct = default);
    void SignOut();
}
