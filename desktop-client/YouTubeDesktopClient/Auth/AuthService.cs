// Auth/AuthService.cs
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace YouTubeDesktopClient.Auth;

public class AuthService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string Scope = "https://www.googleapis.com/auth/youtube";

    private readonly string _clientId;
    private readonly TokenStore _tokenStore;
    private readonly HttpClient _http = new();

    public AuthService(string clientId, TokenStore tokenStore)
    {
        _clientId = clientId;
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
        var refreshToken = _tokenStore.LoadRefreshToken();
        if (refreshToken == null) return null;

        try
        {
            var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            }));
            if (!response.IsSuccessStatusCode) return null;

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return json.GetProperty("access_token").GetString();
        }
        catch (HttpRequestException)
        {
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
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }));
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
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
