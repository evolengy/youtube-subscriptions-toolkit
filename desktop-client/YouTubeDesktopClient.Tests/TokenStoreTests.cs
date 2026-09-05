using Xunit;
using YouTubeDesktopClient.Auth;

public class TokenStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public TokenStoreTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _path = Path.Combine(_tempDir, "token.bin");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadRefreshToken_ReturnsNull_WhenFileDoesNotExist()
    {
        var store = new TokenStore(_path);
        Assert.Null(store.LoadRefreshToken());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPlaintext()
    {
        var store = new TokenStore(_path);
        store.SaveRefreshToken("1//my-refresh-token");

        Assert.Equal("1//my-refresh-token", store.LoadRefreshToken());
    }

    [Fact]
    public void SavedFile_IsNotPlaintextOnDisk()
    {
        var store = new TokenStore(_path);
        store.SaveRefreshToken("1//my-refresh-token");

        var rawBytes = File.ReadAllText(_path);
        Assert.DoesNotContain("1//my-refresh-token", rawBytes);
    }

    [Fact]
    public void Clear_RemovesStoredToken()
    {
        var store = new TokenStore(_path);
        store.SaveRefreshToken("1//my-refresh-token");
        store.Clear();

        Assert.Null(store.LoadRefreshToken());
    }
}
