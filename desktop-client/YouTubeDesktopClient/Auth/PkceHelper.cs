using System.Security.Cryptography;

namespace YouTubeDesktopClient.Auth;

public static class PkceHelper
{
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string DeriveCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
