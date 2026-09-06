// Auth/AuthService.cs
using System.Net;
using System.Net.Http;
using System.Text.Json;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Auth;

public class AuthService : IAuthService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    // youtube.force-ssl is a superset of the plain youtube scope AND is the one
    // comment reads/writes (commentThreads.*, comments.*) require — the plain
    // scope 403s them with ACCESS_TOKEN_SCOPE_INSUFFICIENT. Changing this
    // invalidates old consent: users must re-authorize once.
    private const string Scope = "https://www.googleapis.com/auth/youtube.force-ssl";

    private readonly string _clientId;
    // Google's token endpoint requires client_secret even for a "Desktop
    // app" (installed application) OAuth client using PKCE, despite such
    // clients being public/secret-less per plain RFC 8252. Google's own
    // docs for installed apps say this value "is not treated as a secret"
    // and is fine to embed in source, unlike a Web application secret.
    private readonly string _clientSecret;
    private readonly TokenStore _tokenStore;
    private readonly HttpClient _http = new();

    public AuthService(string clientId, string clientSecret, TokenStore tokenStore)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenStore = tokenStore;
    }

    public async Task<string> SignInInteractiveAsync(CancellationToken ct = default)
    {
        var verifier = PkceHelper.GenerateCodeVerifier();
        var challenge = PkceHelper.DeriveCodeChallenge(verifier);

        using var listener = new HttpListener();
        var port = GetFreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authUrl = $"{AuthEndpoint}?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scope)}&code_challenge={challenge}" +
            "&code_challenge_method=S256&access_type=offline&prompt=consent";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });

        var context = await listener.GetContextAsync().WaitAsync(ct);
        var code = context.Request.QueryString["code"]
            ?? throw new InvalidOperationException("Google did not return an authorization code.");

        var responseBody = "<html><body>Signed in — you can close this window.</body></html>";
        var buffer = System.Text.Encoding.UTF8.GetBytes(responseBody);
        context.Response.OutputStream.Write(buffer);
        context.Response.Close();
        listener.Stop();

        var tokens = await ExchangeCodeAsync(code, verifier, redirectUri);
        _tokenStore.SaveRefreshToken(tokens.RefreshToken);
        return tokens.AccessToken;
    }

    public async Task<string?> GetAccessTokenSilentAsync()
    {
        try
        {
            // LoadRefreshToken is inside the try because DPAPI is user+machine
            // scoped: a token file copied from another machine/profile, or one
            // truncated by a crash, throws CryptographicException here.
            var refreshToken = _tokenStore.LoadRefreshToken();
            if (refreshToken == null) return null;

            var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            }));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError($"Silent token refresh rejected: {(int)response.StatusCode} {body}");
                return null;
            }

            var json = JsonDocument.Parse(body).RootElement;
            return json.GetProperty("access_token").GetString();
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // An undecryptable token file will never become decryptable, so
            // drop it rather than failing this way on every future sync tick.
            Logger.LogError("Stored refresh token could not be decrypted; clearing it", ex);
            _tokenStore.Clear();
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            // A malformed 2xx response (bad JSON, missing access_token) must
            // also come back as "not signed in" rather than throw — this
            // runs inside BackgroundSyncService's Timer callback (Task 8),
            // and while that callback now catches, returning null here keeps
            // "token unavailable" a normal outcome rather than an error.
            Logger.LogError("Silent token refresh failed", ex);
            return null;
        }
    }

    public void SignOut() => _tokenStore.Clear();

    private async Task<(string AccessToken, string RefreshToken)> ExchangeCodeAsync(
        string code, string verifier, string redirectUri)
    {
        var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogError($"Token exchange failed: {(int)response.StatusCode} {body}");
            throw new InvalidOperationException($"Google rejected the sign-in: {body}");
        }

        var json = JsonDocument.Parse(body).RootElement;
        return (json.GetProperty("access_token").GetString()!, json.GetProperty("refresh_token").GetString()!);
    }

    private static int GetFreeLoopbackPort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
