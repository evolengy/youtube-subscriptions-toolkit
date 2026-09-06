using System.Net;

namespace YouTubeDesktopClient.Api;

/// <summary>
/// Turns an API failure into one user-facing sentence for
/// <see cref="Diagnostics.NotificationCenter"/>. Shared by every view model that
/// makes write calls so the wording stays consistent.
/// </summary>
public static class ApiErrorText
{
    public static string Describe(Exception ex) => ex switch
    {
        YouTubeApiException { StatusCode: HttpStatusCode.Forbidden } e when IsQuota(e) =>
            "YouTube API daily quota is used up — this works again after it resets (~midnight US Pacific).",
        YouTubeApiException { StatusCode: HttpStatusCode.Forbidden } => "YouTube rejected the request.",
        YouTubeApiException { StatusCode: HttpStatusCode.Unauthorized } => "Session expired — sign in again.",
        YouTubeApiException { StatusCode: HttpStatusCode.NotFound } => "That item no longer exists on YouTube.",
        YouTubeApiException => "YouTube returned an error.",
        _ => "Something went wrong.",
    };

    public static bool IsQuota(YouTubeApiException e)
    {
        var body = e.ResponseBody ?? string.Empty;
        return body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
            || body.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase)
            || body.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase);
    }
}
