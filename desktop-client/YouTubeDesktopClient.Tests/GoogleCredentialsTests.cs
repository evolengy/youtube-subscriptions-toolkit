using Xunit;
using YouTubeDesktopClient.Auth;

public class GoogleCredentialsTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string Write(string contents)
    {
        var path = Path.Combine(_tempDir, "credentials.json");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Load_ReturnsNull_WhenFileMissing()
    {
        Assert.Null(GoogleCredentials.Load(Path.Combine(_tempDir, "nope.json")));
    }

    [Fact]
    public void Load_ReturnsNull_ForTheUntouchedTemplate()
    {
        var path = Write("""
            { "clientId": "REPLACE_WITH_YOUR_OAUTH_CLIENT_ID.apps.googleusercontent.com",
              "clientSecret": "REPLACE_WITH_YOUR_OAUTH_CLIENT_SECRET" }
            """);
        Assert.Null(GoogleCredentials.Load(path));
    }

    [Fact]
    public void Load_ReturnsNull_WhenAFieldIsBlank()
    {
        var path = Write("""{ "clientId": "123.apps.googleusercontent.com", "clientSecret": "  " }""");
        Assert.Null(GoogleCredentials.Load(path));
    }

    [Fact]
    public void Load_ReturnsNull_OnMalformedJson()
    {
        Assert.Null(GoogleCredentials.Load(Write("{ not json")));
    }

    [Fact]
    public void Load_ReturnsTrimmedValues_WhenFilledIn()
    {
        var path = Write("""
            { "clientId": " 123-abc.apps.googleusercontent.com ", "clientSecret": " GOCSPX-secret " }
            """);
        var creds = GoogleCredentials.Load(path);
        Assert.NotNull(creds);
        Assert.Equal("123-abc.apps.googleusercontent.com", creds!.ClientId);
        Assert.Equal("GOCSPX-secret", creds.ClientSecret);
    }
}
