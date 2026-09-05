using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YouTubeDesktopClient.Auth;

public class TokenStore
{
    private readonly string _filePath;

    public TokenStore(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    }

    public void SaveRefreshToken(string token)
    {
        var plainBytes = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }

    public string? LoadRefreshToken()
    {
        if (!File.Exists(_filePath)) return null;
        var encrypted = File.ReadAllBytes(_filePath);
        var plainBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public void Clear()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }
}
