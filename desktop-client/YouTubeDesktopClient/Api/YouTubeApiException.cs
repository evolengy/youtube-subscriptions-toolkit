// Api/YouTubeApiException.cs
using System.Net;

namespace YouTubeDesktopClient.Api;

/// <summary>
/// Thrown when the YouTube Data API answers a read request with a non-success
/// status (401 expired token, 403 quota exceeded, 5xx, an HTML error page from
/// a captive portal, ...). Without this, the response body would be handed
/// straight to <see cref="System.Text.Json.JsonDocument"/> and surface as a
/// confusing JsonException/KeyNotFoundException far from the real cause.
/// </summary>
public class YouTubeApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public YouTubeApiException(HttpStatusCode statusCode, string? responseBody)
        : base($"YouTube API request failed with status {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
