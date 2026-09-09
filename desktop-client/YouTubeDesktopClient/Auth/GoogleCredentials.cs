using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YouTubeDesktopClient.Logging;

namespace YouTubeDesktopClient.Auth;

/// <summary>
/// OAuth client id + secret for the "Desktop app" client, read from
/// <c>credentials.json</c> in the app-data folder. Never committed — each user
/// brings their own Google Cloud client so they get their own quota and
/// consent screen (see README). Google's own docs say an installed-app client
/// secret "is not treated as a secret", but it is still per-user config, not
/// something to ship in the repo.
/// </summary>
public sealed record GoogleCredentials(string ClientId, string ClientSecret)
{
    public const string PlaceholderMarker = "REPLACE_WITH_YOUR_OAUTH_CLIENT_ID";

    /// <summary>
    /// Loads the file at <paramref name="path"/>. Returns <c>null</c> when it is
    /// missing, unreadable, or still holds the placeholder values from
    /// <c>credentials.example.json</c> — the caller shows the setup help in that
    /// case rather than trying to sign in with junk.
    /// </summary>
    public static GoogleCredentials? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
            var id = dto?.ClientId?.Trim();
            var secret = dto?.ClientSecret?.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)) return null;
            if (id.Contains(PlaceholderMarker) || secret.Contains(PlaceholderMarker)) return null;
            return new GoogleCredentials(id, secret);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not read {path}", ex);
            return null;
        }
    }

    private sealed record Dto(
        [property: JsonPropertyName("clientId")] string? ClientId,
        [property: JsonPropertyName("clientSecret")] string? ClientSecret);
}
