# Desktop YouTube Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Windows desktop YouTube client (.NET 10, WPF) that reproduces the browser extension's subscription-groups/feed/dead-channel functionality natively, plus embedded full-page video/home playback via WebView2 — without touching YouTube's own DOM anywhere.

**Architecture:** Pure-logic pieces (duration parsing/classification, JSON storage, PKCE, token encryption, quota-aware sync decisions) are ported as testable C# classes with no UI dependency. WPF views bind to view models that consume those pieces. WebView2 hosts unmodified youtube.com pages for anything the public API can't provide (playback, home feed, recommendations).

**Tech Stack:** .NET 10 (LTS), WPF, `Microsoft.Web.WebView2`, `System.Text.Json`, xUnit for tests. No mocking library — a hand-written fake `HttpMessageHandler` covers API-client tests, keeping dependencies minimal.

**Spec:** [docs/superpowers/specs/2026-09-05-desktop-youtube-client-design.md](../specs/2026-09-05-desktop-youtube-client-design.md)

## Global Constraints

- .NET 10 (LTS) — not .NET 8 or 9.
- Storage: two JSON files under `%AppData%\YouTubeSubscriptionsToolkit\` — `settings.json` (groups, watched ids) and `cache.json` (subscriptions cache, videos cache, last-synced timestamp, per-channel ETags).
- OAuth client type: **Desktop app**, Authorization Code + PKCE, loopback redirect `http://127.0.0.1:{port}/`.
- `refresh_token` encrypted at rest via Windows DPAPI (`System.Security.Cryptography.ProtectedData`, current-user scope).
- Default background sync interval: **3 hours**, using ETag-conditional `playlistItems.list` requests to avoid burning quota on unchanged channels.
- Shorts threshold: duration ≤ 60 seconds (same heuristic already approved and shipped in the browser extension).
- All new project files live under `desktop-client/` at the repo root, alongside the existing extension files (this is a separate app, not a replacement).

---

## File Structure

```
desktop-client/
  YouTubeDesktopClient.sln
  YouTubeDesktopClient/
    YouTubeDesktopClient.csproj
    App.xaml / App.xaml.cs
    MainWindow.xaml / MainWindow.xaml.cs
    Auth/
      PkceHelper.cs
      TokenStore.cs
      AuthService.cs
    Api/
      Models/ApiModels.cs
      YouTubeApiClient.cs
    Storage/
      Models/StorageModels.cs
      SubscriptionStore.cs
    Feed/
      VideoClassifier.cs
      FeedViewModel.cs
    Groups/
      GroupsViewModel.cs
    Channels/
      ChannelManagementViewModel.cs
    Sync/
      BackgroundSyncService.cs
    Tabs/
      TabsViewModel.cs
    Tray/
      TrayIconService.cs
      StartupRegistration.cs
    Views/
      GroupsPanel.xaml(.cs)
      FeedPanel.xaml(.cs)
      ChannelManagementPanel.xaml(.cs)
  YouTubeDesktopClient.Tests/
    YouTubeDesktopClient.Tests.csproj
    VideoClassifierTests.cs
    SubscriptionStoreTests.cs
    PkceHelperTests.cs
    TokenStoreTests.cs
    YouTubeApiClientTests.cs
    BackgroundSyncServiceTests.cs
    FeedViewModelTests.cs
    GroupsViewModelTests.cs
    ChannelManagementViewModelTests.cs
    TabsViewModelTests.cs
    StartupRegistrationTests.cs
```

---

### Task 1: Project scaffolding

**Files:**
- Create: `desktop-client/YouTubeDesktopClient.sln`
- Create: `desktop-client/YouTubeDesktopClient/YouTubeDesktopClient.csproj`
- Create: `desktop-client/YouTubeDesktopClient/App.xaml`
- Create: `desktop-client/YouTubeDesktopClient/App.xaml.cs`
- Create: `desktop-client/YouTubeDesktopClient/MainWindow.xaml`
- Create: `desktop-client/YouTubeDesktopClient/MainWindow.xaml.cs`
- Create: `desktop-client/YouTubeDesktopClient.Tests/YouTubeDesktopClient.Tests.csproj`

**Interfaces:**
- Produces: a buildable, runnable WPF app shell and an empty xUnit test project referencing it, for every later task to add to.

- [ ] **Step 1: Create the solution and WPF project**

```bash
cd desktop-client
dotnet new sln -n YouTubeDesktopClient
dotnet new wpf -n YouTubeDesktopClient -f net10.0
dotnet sln add YouTubeDesktopClient/YouTubeDesktopClient.csproj
```

- [ ] **Step 2: Add the WebView2 package**

```bash
dotnet add YouTubeDesktopClient/YouTubeDesktopClient.csproj package Microsoft.Web.WebView2
```

- [ ] **Step 3: Create the test project and reference the app**

```bash
dotnet new xunit -n YouTubeDesktopClient.Tests -f net10.0
dotnet sln add YouTubeDesktopClient.Tests/YouTubeDesktopClient.Tests.csproj
dotnet add YouTubeDesktopClient.Tests/YouTubeDesktopClient.Tests.csproj reference YouTubeDesktopClient/YouTubeDesktopClient.csproj
```

`YouTubeDesktopClient.csproj` must have `<OutputType>WinExe</OutputType>` and `<UseWPF>true</UseWPF>` (the `dotnet new wpf` template sets these) so the test project can still reference its classes as a library — no changes needed if the template output is used as-is.

- [ ] **Step 4: Verify the solution builds and tests run**

Run: `dotnet build desktop-client/YouTubeDesktopClient.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln`
Expected: Passed! (the template's placeholder test, or 0 tests if you deleted it — either is fine at this stage)

- [ ] **Step 5: Commit**

```bash
git add desktop-client
git commit -m "chore: scaffold WPF desktop client project and test project"
```

---

### Task 2: Video duration parsing and type classification

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Feed/VideoClassifier.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/VideoClassifierTests.cs`

**Interfaces:**
- Produces: `VideoClassifier.ParseIsoDuration(string iso) -> int` (seconds), `VideoClassifier.ClassifyVideoType(string liveBroadcastContent, int durationSeconds) -> string` (`"live" | "short" | "video"`). Both are static, stateless — used later by `FeedViewModel`.

- [ ] **Step 1: Write the failing tests**

```csharp
// VideoClassifierTests.cs
using Xunit;
using YouTubeDesktopClient.Feed;

public class VideoClassifierTests
{
    [Theory]
    [InlineData("PT4M13S", 253)]
    [InlineData("PT1H2M3S", 3723)]
    [InlineData("PT45S", 45)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseIsoDuration_ParsesCorrectly(string iso, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, VideoClassifier.ParseIsoDuration(iso));
    }

    [Fact]
    public void ClassifyVideoType_LiveTakesPriorityOverDuration()
    {
        Assert.Equal("live", VideoClassifier.ClassifyVideoType("live", 30));
    }

    [Fact]
    public void ClassifyVideoType_UpcomingIsLive()
    {
        Assert.Equal("live", VideoClassifier.ClassifyVideoType("upcoming", 0));
    }

    [Fact]
    public void ClassifyVideoType_ShortAtOrUnderThreshold()
    {
        Assert.Equal("short", VideoClassifier.ClassifyVideoType("none", 60));
    }

    [Fact]
    public void ClassifyVideoType_VideoOverThreshold()
    {
        Assert.Equal("video", VideoClassifier.ClassifyVideoType("none", 61));
    }

    [Fact]
    public void ClassifyVideoType_EndedLiveStreamFallsBackToDuration()
    {
        // liveBroadcastContent "none" means the stream ended; classification
        // then falls through to the ordinary duration check, same as any
        // past upload.
        Assert.Equal("video", VideoClassifier.ClassifyVideoType("none", 5000));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter VideoClassifierTests`
Expected: FAIL — `VideoClassifier` does not exist.

- [ ] **Step 3: Implement**

```csharp
// VideoClassifier.cs
using System.Text.RegularExpressions;

namespace YouTubeDesktopClient.Feed;

public static class VideoClassifier
{
    private const int ShortMaxSeconds = 60;
    private static readonly Regex DurationPattern =
        new(@"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$", RegexOptions.Compiled);

    public static int ParseIsoDuration(string? iso)
    {
        var match = DurationPattern.Match(iso ?? "");
        if (!match.Success) return 0;

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return hours * 3600 + minutes * 60 + seconds;
    }

    // liveBroadcastContent is checked first because a live/upcoming stream
    // can report a near-zero or stale duration while airing. Once a stream
    // has ended (liveBroadcastContent == "none"), it falls through to the
    // ordinary duration check like any past upload.
    public static string ClassifyVideoType(string liveBroadcastContent, int durationSeconds)
    {
        if (liveBroadcastContent == "live" || liveBroadcastContent == "upcoming")
            return "live";
        return durationSeconds <= ShortMaxSeconds ? "short" : "video";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter VideoClassifierTests`
Expected: Passed! 7 tests.

- [ ] **Step 5: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Feed/VideoClassifier.cs desktop-client/YouTubeDesktopClient.Tests/VideoClassifierTests.cs
git commit -m "feat: port duration parsing and video type classification from extension"
```

---

### Task 3: JSON-backed storage (SubscriptionStore)

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Storage/Models/StorageModels.cs`
- Create: `desktop-client/YouTubeDesktopClient/Storage/SubscriptionStore.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/SubscriptionStoreTests.cs`

**Interfaces:**
- Produces:
  - `record GroupData(string Name, List<string> ChannelIds)`
  - `record SubscriptionCacheEntry(string Title, string? Thumbnail, string? Country, string? UploadsPlaylistId, bool Dead, string? PlaylistEtag, string? SubscriptionId)`
  - `record VideoInfo(string VideoId, string ChannelId, string Title, string? Thumbnail, DateTimeOffset PublishedAt, string Duration, long ViewCount, string LiveBroadcastContent)`
  - `class SubscriptionStore(string settingsPath, string cachePath)` with methods:
    - `Dictionary<string, GroupData> GetGroups()`
    - `void SaveGroups(Dictionary<string, GroupData> groups)`
    - `HashSet<string> GetWatchedVideoIds()`
    - `void MarkVideoWatched(string videoId)`
    - `Dictionary<string, SubscriptionCacheEntry> GetSubscriptionsCache()`
    - `void SaveSubscriptionsCache(Dictionary<string, SubscriptionCacheEntry> cache)`
    - `Dictionary<string, List<VideoInfo>> GetVideosCache()`
    - `void SaveVideosCache(Dictionary<string, List<VideoInfo>> cache)`
    - `DateTimeOffset? GetLastSyncedAt()`
    - `void SetLastSyncedAt(DateTimeOffset timestamp)`
- Consumed later by: `FeedViewModel`, `GroupsViewModel`, `ChannelManagementViewModel`, `BackgroundSyncService`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SubscriptionStoreTests.cs
using System.Collections.Generic;
using Xunit;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class SubscriptionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public SubscriptionStoreTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(
            Path.Combine(_tempDir, "settings.json"),
            Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetGroups_ReturnsEmptyDictionary_WhenFileDoesNotExist()
    {
        Assert.Empty(_store.GetGroups());
    }

    [Fact]
    public void SaveGroups_ThenGetGroups_RoundTrips()
    {
        var groups = new Dictionary<string, GroupData>
        {
            ["g1"] = new GroupData("Music", new List<string> { "UC1", "UC2" }),
        };

        _store.SaveGroups(groups);
        var loaded = _store.GetGroups();

        Assert.Single(loaded);
        Assert.Equal("Music", loaded["g1"].Name);
        Assert.Equal(new[] { "UC1", "UC2" }, loaded["g1"].ChannelIds);
    }

    [Fact]
    public void MarkVideoWatched_AddsToWatchedSet()
    {
        _store.MarkVideoWatched("v1");
        _store.MarkVideoWatched("v2");

        var watched = _store.GetWatchedVideoIds();

        Assert.Contains("v1", watched);
        Assert.Contains("v2", watched);
    }

    [Fact]
    public void MarkVideoWatched_CapsListAtTwoThousand()
    {
        for (int i = 0; i < 2005; i++)
            _store.MarkVideoWatched($"v{i}");

        Assert.Equal(2000, _store.GetWatchedVideoIds().Count);
        Assert.DoesNotContain("v0", _store.GetWatchedVideoIds());
        Assert.Contains("v2004", _store.GetWatchedVideoIds());
    }

    [Fact]
    public void SubscriptionsCache_RoundTrips()
    {
        var cache = new Dictionary<string, SubscriptionCacheEntry>
        {
            ["UC1"] = new SubscriptionCacheEntry("Some Channel", "http://thumb", "US", "UUxyz", false, "etag123", "sub1"),
        };

        _store.SaveSubscriptionsCache(cache);
        var loaded = _store.GetSubscriptionsCache();

        Assert.Equal("Some Channel", loaded["UC1"].Title);
        Assert.Equal("etag123", loaded["UC1"].PlaylistEtag);
    }

    [Fact]
    public void LastSyncedAt_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        _store.SetLastSyncedAt(now);

        Assert.Equal(now, _store.GetLastSyncedAt());
    }

    [Fact]
    public void GetLastSyncedAt_ReturnsNull_WhenNeverSet()
    {
        Assert.Null(_store.GetLastSyncedAt());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter SubscriptionStoreTests`
Expected: FAIL — `SubscriptionStore` and the model records don't exist.

- [ ] **Step 3: Implement the models**

```csharp
// Storage/Models/StorageModels.cs
namespace YouTubeDesktopClient.Storage.Models;

public record GroupData(string Name, List<string> ChannelIds);

public record SubscriptionCacheEntry(
    string Title,
    string? Thumbnail,
    string? Country,
    string? UploadsPlaylistId,
    bool Dead,
    string? PlaylistEtag,
    string? SubscriptionId);

public record VideoInfo(
    string VideoId,
    string ChannelId,
    string Title,
    string? Thumbnail,
    DateTimeOffset PublishedAt,
    string Duration,
    long ViewCount,
    string LiveBroadcastContent);

internal record SettingsFile(
    Dictionary<string, GroupData> Groups,
    List<string> WatchedVideoIds);

internal record CacheFile(
    Dictionary<string, SubscriptionCacheEntry> SubscriptionsCache,
    Dictionary<string, List<VideoInfo>> VideosCache,
    DateTimeOffset? LastSyncedAt);
```

- [ ] **Step 4: Implement the store**

```csharp
// Storage/SubscriptionStore.cs
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Storage;

public class SubscriptionStore
{
    private const int MaxWatchedIds = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly string _cachePath;

    public SubscriptionStore(string settingsPath, string cachePath)
    {
        _settingsPath = settingsPath;
        _cachePath = cachePath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
    }

    private SettingsFile ReadSettings() =>
        File.Exists(_settingsPath)
            ? JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_settingsPath))!
            : new SettingsFile(new(), new());

    private void WriteSettings(SettingsFile settings) =>
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));

    private CacheFile ReadCache() =>
        File.Exists(_cachePath)
            ? JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath))!
            : new CacheFile(new(), new(), null);

    private void WriteCache(CacheFile cache) =>
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, JsonOptions));

    public Dictionary<string, GroupData> GetGroups() => ReadSettings().Groups;

    public void SaveGroups(Dictionary<string, GroupData> groups)
    {
        var settings = ReadSettings();
        WriteSettings(settings with { Groups = groups });
    }

    public HashSet<string> GetWatchedVideoIds() => new(ReadSettings().WatchedVideoIds);

    public void MarkVideoWatched(string videoId)
    {
        var settings = ReadSettings();
        var ids = new List<string>(settings.WatchedVideoIds);
        ids.Remove(videoId);
        ids.Add(videoId);
        if (ids.Count > MaxWatchedIds)
            ids = ids.GetRange(ids.Count - MaxWatchedIds, MaxWatchedIds);
        WriteSettings(settings with { WatchedVideoIds = ids });
    }

    public Dictionary<string, SubscriptionCacheEntry> GetSubscriptionsCache() =>
        ReadCache().SubscriptionsCache;

    public void SaveSubscriptionsCache(Dictionary<string, SubscriptionCacheEntry> cache)
    {
        var current = ReadCache();
        WriteCache(current with { SubscriptionsCache = cache });
    }

    public Dictionary<string, List<VideoInfo>> GetVideosCache() => ReadCache().VideosCache;

    public void SaveVideosCache(Dictionary<string, List<VideoInfo>> cache)
    {
        var current = ReadCache();
        WriteCache(current with { VideosCache = cache });
    }

    public DateTimeOffset? GetLastSyncedAt() => ReadCache().LastSyncedAt;

    public void SetLastSyncedAt(DateTimeOffset timestamp)
    {
        var current = ReadCache();
        WriteCache(current with { LastSyncedAt = timestamp });
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter SubscriptionStoreTests`
Expected: Passed! 7 tests.

- [ ] **Step 6: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Storage
git add desktop-client/YouTubeDesktopClient.Tests/SubscriptionStoreTests.cs
git commit -m "feat: add JSON-backed subscription store"
```

---

### Task 4: PKCE helper for OAuth

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Auth/PkceHelper.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/PkceHelperTests.cs`

**Interfaces:**
- Produces: `static class PkceHelper` with `GenerateCodeVerifier() -> string` and `DeriveCodeChallenge(string verifier) -> string` (S256 method per RFC 7636). Consumed by `AuthService` in Task 6.

- [ ] **Step 1: Write the failing tests**

```csharp
// PkceHelperTests.cs
using Xunit;
using YouTubeDesktopClient.Auth;

public class PkceHelperTests
{
    [Fact]
    public void GenerateCodeVerifier_ProducesUrlSafeStringOfSufficientLength()
    {
        var verifier = PkceHelper.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesDifferentValuesEachCall()
    {
        Assert.NotEqual(PkceHelper.GenerateCodeVerifier(), PkceHelper.GenerateCodeVerifier());
    }

    [Fact]
    public void DeriveCodeChallenge_IsDeterministicForSameVerifier()
    {
        var verifier = "test-verifier-1234567890123456789012345";

        Assert.Equal(PkceHelper.DeriveCodeChallenge(verifier), PkceHelper.DeriveCodeChallenge(verifier));
    }

    [Fact]
    public void DeriveCodeChallenge_MatchesKnownRfc7636Example()
    {
        // From RFC 7636 Appendix B.
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        Assert.Equal(expected, PkceHelper.DeriveCodeChallenge(verifier));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter PkceHelperTests`
Expected: FAIL — `PkceHelper` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Auth/PkceHelper.cs
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter PkceHelperTests`
Expected: Passed! 4 tests.

- [ ] **Step 5: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Auth/PkceHelper.cs desktop-client/YouTubeDesktopClient.Tests/PkceHelperTests.cs
git commit -m "feat: add PKCE code verifier/challenge generation"
```

---

### Task 5: Encrypted token storage

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Auth/TokenStore.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/TokenStoreTests.cs`

**Interfaces:**
- Produces: `class TokenStore(string filePath)` with `void SaveRefreshToken(string token)`, `string? LoadRefreshToken()`, `void Clear()`. Consumed by `AuthService` in Task 6.

- [ ] **Step 1: Write the failing tests**

```csharp
// TokenStoreTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter TokenStoreTests`
Expected: FAIL — `TokenStore` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Auth/TokenStore.cs
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
```

Note: `SaveRefreshToken`/`LoadRefreshToken` write/read raw bytes, but the test reads the file with `File.ReadAllText` (interpreting bytes as text) purely to assert the plaintext token string doesn't appear anywhere in it — that's valid regardless of encoding since DPAPI ciphertext won't contain the original ASCII substring.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter TokenStoreTests`
Expected: Passed! 4 tests.

- [ ] **Step 5: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Auth/TokenStore.cs desktop-client/YouTubeDesktopClient.Tests/TokenStoreTests.cs
git commit -m "feat: add DPAPI-encrypted refresh token storage"
```

---

### Task 6: YouTube API client

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Api/Models/ApiModels.cs`
- Create: `desktop-client/YouTubeDesktopClient/Api/YouTubeApiClient.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/YouTubeApiClientTests.cs`

**Interfaces:**
- Consumes: `System.Net.Http.HttpClient` (injected, so tests can supply a fake handler).
- Produces:
  - `record SubscriptionEntry(string SubscriptionId, string ChannelId, string Title, string? Thumbnail)`
  - `record ChannelDetails(string ChannelId, string? Country, string UploadsPlaylistId)`
  - `record PlaylistItemsResult(List<string> VideoIds, string? Etag, bool NotModified)`
  - `class YouTubeApiClient(HttpClient httpClient)` with:
    - `Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken)`
    - `Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds)`
    - `Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15)`
    - `Task<List<Storage.Models.VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds)`
    - `Task UnsubscribeAsync(string accessToken, string subscriptionId)`
- Consumed by: `BackgroundSyncService` (Task 7), `ChannelManagementViewModel` (Task 10).

- [ ] **Step 1: Write the failing tests**

```csharp
// YouTubeApiClientTests.cs
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using YouTubeDesktopClient.Api;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}

public class YouTubeApiClientTests
{
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task FetchAllSubscriptionsAsync_ParsesSingleFullPage()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "items": [
            { "id": "sub1", "snippet": { "title": "Channel One",
              "resourceId": { "channelId": "UC1" },
              "thumbnails": { "default": { "url": "http://t1" } } } }
          ]
        }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchAllSubscriptionsAsync("token");

        Assert.Single(result);
        Assert.Equal("UC1", result[0].ChannelId);
        Assert.Equal("Channel One", result[0].Title);
        Assert.Equal("sub1", result[0].SubscriptionId);
    }

    [Fact]
    public async Task FetchAllSubscriptionsAsync_FollowsPagination()
    {
        int callCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return JsonResponse("""
                {
                  "nextPageToken": "page2",
                  "items": [
                    { "id": "sub1", "snippet": { "title": "A", "resourceId": { "channelId": "UC1" }, "thumbnails": {} } }
                  ]
                }
                """);
            }
            return JsonResponse("""
            {
              "items": [
                { "id": "sub2", "snippet": { "title": "B", "resourceId": { "channelId": "UC2" }, "thumbnails": {} } }
              ]
            }
            """);
        });
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchAllSubscriptionsAsync("token");

        Assert.Equal(2, callCount);
        Assert.Equal(2, result.Count);
        Assert.Equal("UC2", result[1].ChannelId);
    }

    [Fact]
    public async Task FetchChannelsDetailsAsync_MapsCountryAndUploadsPlaylist()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""
        {
          "items": [
            { "id": "UC1", "snippet": { "country": "US" },
              "contentDetails": { "relatedPlaylists": { "uploads": "UU1" } } }
          ]
        }
        """));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchChannelsDetailsAsync("token", new List<string> { "UC1" });

        Assert.Equal("US", result["UC1"].Country);
        Assert.Equal("UU1", result["UC1"].UploadsPlaylistId);
    }

    [Fact]
    public async Task FetchChannelsDetailsAsync_MissingChannelIsOmitted()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{ "items": [] }"""));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchChannelsDetailsAsync("token", new List<string> { "UCdead" });

        Assert.False(result.ContainsKey("UCdead"));
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_SendsIfNoneMatchHeader_WhenEtagProvided()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("""{ "items": [] }"""));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: "\"abc\"");

        Assert.Equal("\"abc\"", handler.LastRequest!.Headers.IfNoneMatch.ToString());
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_ReturnsNotModified_On304()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: "\"abc\"");

        Assert.True(result.NotModified);
        Assert.Empty(result.VideoIds);
    }

    [Fact]
    public async Task FetchRecentUploadIdsAsync_ReturnsVideoIdsAndEtag()
    {
        var response = JsonResponse("""{ "items": [ { "contentDetails": { "videoId": "v1" } } ] }""");
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"newetag\"");
        var handler = new FakeHttpMessageHandler(_ => response);
        var client = new YouTubeApiClient(new HttpClient(handler));

        var result = await client.FetchRecentUploadIdsAsync("token", "UU1", previousEtag: null);

        Assert.False(result.NotModified);
        Assert.Equal(new[] { "v1" }, result.VideoIds);
        Assert.Equal("\"newetag\"", result.Etag);
    }

    [Fact]
    public async Task UnsubscribeAsync_SendsDeleteWithSubscriptionId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new YouTubeApiClient(new HttpClient(handler));

        await client.UnsubscribeAsync("token", "sub123");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("id=sub123", handler.LastRequest.RequestUri!.Query);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter YouTubeApiClientTests`
Expected: FAIL — `YouTubeApiClient` and its models don't exist.

- [ ] **Step 3: Implement the models**

```csharp
// Api/Models/ApiModels.cs
namespace YouTubeDesktopClient.Api;

public record SubscriptionEntry(string SubscriptionId, string ChannelId, string Title, string? Thumbnail);

public record ChannelDetails(string ChannelId, string? Country, string UploadsPlaylistId);

public record PlaylistItemsResult(List<string> VideoIds, string? Etag, bool NotModified);
```

- [ ] **Step 4: Implement the client**

```csharp
// Api/YouTubeApiClient.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Api;

public class YouTubeApiClient
{
    private const string ApiBase = "https://www.googleapis.com/youtube/v3";
    private readonly HttpClient _http;

    public YouTubeApiClient(HttpClient httpClient) => _http = httpClient;

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string accessToken,
        Dictionary<string, string?> queryParams)
    {
        var query = string.Join("&", queryParams
            .Where(kv => kv.Value != null)
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        var request = new HttpRequestMessage(method, $"{ApiBase}/{path}?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    public async Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken)
    {
        var results = new List<SubscriptionEntry>();
        string? pageToken = null;
        do
        {
            var request = BuildRequest(HttpMethod.Get, "subscriptions", accessToken, new()
            {
                ["part"] = "snippet",
                ["mine"] = "true",
                ["maxResults"] = "50",
                ["pageToken"] = pageToken,
            });
            using var response = await _http.SendAsync(request);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");
                results.Add(new SubscriptionEntry(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("resourceId").GetProperty("channelId").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    thumbnails.TryGetProperty("default", out var def) ? def.GetProperty("url").GetString() : null));
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (pageToken != null);

        return results;
    }

    public async Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(
        string accessToken, List<string> channelIds)
    {
        var found = new Dictionary<string, ChannelDetails>();
        foreach (var batch in Chunk(channelIds, 50))
        {
            var request = BuildRequest(HttpMethod.Get, "channels", accessToken, new()
            {
                ["part"] = "snippet,contentDetails",
                ["id"] = string.Join(",", batch),
                ["maxResults"] = "50",
            });
            using var response = await _http.SendAsync(request);
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                var snippet = item.GetProperty("snippet");
                var uploads = item.GetProperty("contentDetails").GetProperty("relatedPlaylists").GetProperty("uploads").GetString()!;
                found[id] = new ChannelDetails(
                    id,
                    snippet.TryGetProperty("country", out var c) ? c.GetString() : null,
                    uploads);
            }
        }
        return found;
    }

    public async Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(
        string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15)
    {
        var request = BuildRequest(HttpMethod.Get, "playlistItems", accessToken, new()
        {
            ["part"] = "contentDetails",
            ["playlistId"] = uploadsPlaylistId,
            ["maxResults"] = maxResults.ToString(),
        });
        if (previousEtag != null)
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(previousEtag));

        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new PlaylistItemsResult(new List<string>(), previousEtag, NotModified: true);

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var videoIds = root.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("contentDetails").GetProperty("videoId").GetString()!)
            .ToList();
        var etag = response.Headers.ETag?.ToString();
        return new PlaylistItemsResult(videoIds, etag, NotModified: false);
    }

    public async Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds)
    {
        var results = new List<VideoInfo>();
        foreach (var batch in Chunk(videoIds, 50))
        {
            var request = BuildRequest(HttpMethod.Get, "videos", accessToken, new()
            {
                ["part"] = "snippet,contentDetails,statistics,liveStreamingDetails",
                ["id"] = string.Join(",", batch),
                ["maxResults"] = "50",
            });
            using var response = await _http.SendAsync(request);
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var thumbnails = snippet.GetProperty("thumbnails");
                var statistics = item.TryGetProperty("statistics", out var s) ? s : default;
                results.Add(new VideoInfo(
                    item.GetProperty("id").GetString()!,
                    snippet.GetProperty("channelId").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    thumbnails.TryGetProperty("medium", out var med) ? med.GetProperty("url").GetString() : null,
                    snippet.GetProperty("publishedAt").GetDateTimeOffset(),
                    item.GetProperty("contentDetails").GetProperty("duration").GetString()!,
                    statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("viewCount", out var vc)
                        ? long.Parse(vc.GetString()!) : 0,
                    snippet.GetProperty("liveBroadcastContent").GetString()!));
            }
        }
        return results;
    }

    public async Task UnsubscribeAsync(string accessToken, string subscriptionId)
    {
        var request = BuildRequest(HttpMethod.Delete, "subscriptions", accessToken, new()
        {
            ["id"] = subscriptionId,
        });
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (int i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter YouTubeApiClientTests`
Expected: Passed! 8 tests.

- [ ] **Step 6: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Api
git add desktop-client/YouTubeDesktopClient.Tests/YouTubeApiClientTests.cs
git commit -m "feat: add YouTube Data API client with ETag-conditional playlist fetch"
```

---

### Task 7: OAuth desktop flow (AuthService)

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Auth/AuthService.cs`

**Interfaces:**
- Consumes: `PkceHelper` (Task 4), `TokenStore` (Task 5).
- Produces: `class AuthService(string clientId, TokenStore tokenStore)` with:
  - `Task<string> SignInInteractiveAsync(CancellationToken ct = default)` — opens the system browser, runs the loopback listener, returns a fresh access token, and persists the refresh token via `TokenStore`.
  - `Task<string?> GetAccessTokenSilentAsync()` — uses the stored refresh token to mint a new access token without any UI; returns `null` if there is no stored refresh token or the refresh call fails.
  - `void SignOut()` — clears the stored refresh token.
- Consumed by: `App.xaml.cs` / `MainWindow.xaml.cs` (Task 11), `BackgroundSyncService` (Task 8).

This task is not unit-tested — it requires a real browser round trip to Google, which is exactly the kind of interactive OAuth flow the spec's Testing section calls out for manual verification rather than automated tests. `PkceHelper` and `TokenStore`, which carry the parts that *can* be tested in isolation, already have their own test coverage from Tasks 4–5.

- [ ] **Step 1: Implement AuthService**

```csharp
// Auth/AuthService.cs
using System.Net;
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
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build desktop-client/YouTubeDesktopClient.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Auth/AuthService.cs
git commit -m "feat: add desktop OAuth (PKCE + loopback redirect) flow"
```

**Manual verification (do this once the main window exists in Task 11):** trigger sign-in, confirm the system browser opens Google's consent screen, confirm the app receives control back after granting access, and confirm closing the app and reopening it does *not* prompt for sign-in again (silent refresh via the stored `refresh_token` should just work).

---

### Task 8: Background sync service

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Sync/BackgroundSyncService.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/BackgroundSyncServiceTests.cs`

**Interfaces:**
- Consumes: `YouTubeApiClient` (Task 6), `SubscriptionStore` (Task 3), a `Func<Task<string?>> getAccessToken` delegate (so it doesn't need to know about `AuthService` directly — keeps this class testable without any auth machinery).
- Produces: `class BackgroundSyncService(YouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)` with `Task RunOnceAsync()` (does one full refresh cycle) and `void Start(TimeSpan interval)` / `void Stop()` (wraps `RunOnceAsync` in a `System.Threading.Timer`).
- Consumed by: `App.xaml.cs` (Task 11), tray "Refresh now" menu item (Task 12).

- [ ] **Step 1: Write the failing tests**

These tests exercise `RunOnceAsync` against an in-memory fake of the parts of `YouTubeApiClient` it needs. Since `YouTubeApiClient`'s methods aren't virtual, `BackgroundSyncService` depends on a small interface instead — extract `IYouTubeApiClient` from the existing class first.

```csharp
// Api/IYouTubeApiClient.cs (new file, extracted from YouTubeApiClient)
namespace YouTubeDesktopClient.Api;

public interface IYouTubeApiClient
{
    Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken);
    Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds);
    Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15);
    Task<List<Storage.Models.VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds);
    Task UnsubscribeAsync(string accessToken, string subscriptionId);
}
```

Make `YouTubeApiClient : IYouTubeApiClient` (add the interface to its class declaration — no other changes needed since the signatures already match).

```csharp
// BackgroundSyncServiceTests.cs
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;
using YouTubeDesktopClient.Sync;

public class FakeYouTubeApiClient : IYouTubeApiClient
{
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();
    public Dictionary<string, ChannelDetails> Channels { get; set; } = new();
    public Dictionary<string, PlaylistItemsResult> PlaylistResults { get; set; } = new();
    public Dictionary<string, List<VideoInfo>> VideoDetailsByChannel { get; set; } = new();
    public int PlaylistCallCount { get; private set; }

    public Task<List<SubscriptionEntry>> FetchAllSubscriptionsAsync(string accessToken) =>
        Task.FromResult(Subscriptions);

    public Task<Dictionary<string, ChannelDetails>> FetchChannelsDetailsAsync(string accessToken, List<string> channelIds) =>
        Task.FromResult(channelIds.Where(Channels.ContainsKey).ToDictionary(id => id, id => Channels[id]));

    public Task<PlaylistItemsResult> FetchRecentUploadIdsAsync(string accessToken, string uploadsPlaylistId, string? previousEtag, int maxResults = 15)
    {
        PlaylistCallCount++;
        return Task.FromResult(PlaylistResults[uploadsPlaylistId]);
    }

    public Task<List<VideoInfo>> FetchVideosDetailsAsync(string accessToken, List<string> videoIds) =>
        Task.FromResult(VideoDetailsByChannel.Values.SelectMany(v => v).Where(v => videoIds.Contains(v.VideoId)).ToList());

    public Task UnsubscribeAsync(string accessToken, string subscriptionId) => Task.CompletedTask;
}

public class BackgroundSyncServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public BackgroundSyncServiceTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RunOnceAsync_PopulatesSubscriptionsCache()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", "thumb") },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", "US", "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), null, NotModified: false) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        var cache = _store.GetSubscriptionsCache();
        Assert.Equal("Channel One", cache["UC1"].Title);
        Assert.Equal("US", cache["UC1"].Country);
        Assert.False(cache["UC1"].Dead);
    }

    [Fact]
    public async Task RunOnceAsync_MarksChannelDead_WhenMissingFromChannelDetails()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UCgone", "Gone Channel", null) },
            Channels = new(), // empty — channel not found
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.True(_store.GetSubscriptionsCache()["UCgone"].Dead);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsPlaylistCall_WhenAccessTokenUnavailable()
    {
        var api = new FakeYouTubeApiClient();
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>(null));

        await service.RunOnceAsync();

        Assert.Equal(0, api.PlaylistCallCount);
        Assert.Empty(_store.GetSubscriptionsCache());
    }

    [Fact]
    public async Task RunOnceAsync_StoresEtagFromPlaylistResponse_ForNextCycle()
    {
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), "\"newetag\"", NotModified: false) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Equal("\"newetag\"", _store.GetSubscriptionsCache()["UC1"].PlaylistEtag);
    }

    [Fact]
    public async Task RunOnceAsync_PreservesExistingVideos_WhenPlaylistNotModified()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Channel One", null, null, "UU1", false, "\"oldetag\"", "sub1"),
        });
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new List<VideoInfo> { new("v1", "UC1", "Old Video", null, DateTimeOffset.UtcNow, "PT1M", 10, "none") },
        });
        var api = new FakeYouTubeApiClient
        {
            Subscriptions = new() { new SubscriptionEntry("sub1", "UC1", "Channel One", null) },
            Channels = new() { ["UC1"] = new ChannelDetails("UC1", null, "UU1") },
            PlaylistResults = new() { ["UU1"] = new PlaylistItemsResult(new(), "\"oldetag\"", NotModified: true) },
        };
        var service = new BackgroundSyncService(api, _store, () => Task.FromResult<string?>("token"));

        await service.RunOnceAsync();

        Assert.Single(_store.GetVideosCache()["UC1"]);
        Assert.Equal("Old Video", _store.GetVideosCache()["UC1"][0].Title);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter BackgroundSyncServiceTests`
Expected: FAIL — `BackgroundSyncService` and `IYouTubeApiClient` don't exist yet.

- [ ] **Step 3: Extract the interface and implement the service**

```csharp
// Add ": IYouTubeApiClient" to the YouTubeApiClient class declaration in Api/YouTubeApiClient.cs:
public class YouTubeApiClient : IYouTubeApiClient
```

```csharp
// Sync/BackgroundSyncService.cs
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Sync;

public class BackgroundSyncService
{
    private const int VideosPerChannel = 15;

    private readonly IYouTubeApiClient _api;
    private readonly SubscriptionStore _store;
    private readonly Func<Task<string?>> _getAccessToken;
    private Timer? _timer;

    public BackgroundSyncService(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)
    {
        _api = api;
        _store = store;
        _getAccessToken = getAccessToken;
    }

    public void Start(TimeSpan interval)
    {
        _timer = new Timer(_ => RunOnceAsync().GetAwaiter().GetResult(), null, TimeSpan.Zero, interval);
    }

    public void Stop() => _timer?.Dispose();

    public async Task RunOnceAsync()
    {
        var token = await _getAccessToken();
        if (token == null) return;

        var subscriptions = await _api.FetchAllSubscriptionsAsync(token);
        var channelIds = subscriptions.Select(s => s.ChannelId).ToList();
        var channelDetails = await _api.FetchChannelsDetailsAsync(token, channelIds);
        var previousCache = _store.GetSubscriptionsCache();
        var previousVideos = _store.GetVideosCache();

        var subscriptionsCache = new Dictionary<string, SubscriptionCacheEntry>();
        var videosCache = new Dictionary<string, List<VideoInfo>>();

        foreach (var sub in subscriptions)
        {
            channelDetails.TryGetValue(sub.ChannelId, out var details);
            var previousEtag = previousCache.TryGetValue(sub.ChannelId, out var prevEntry) ? prevEntry.PlaylistEtag : null;

            if (details == null)
            {
                subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                    sub.Title, sub.Thumbnail, null, null, Dead: true, previousEtag, sub.SubscriptionId);
                continue;
            }

            var playlistResult = await _api.FetchRecentUploadIdsAsync(
                token, details.UploadsPlaylistId, previousEtag, VideosPerChannel);

            subscriptionsCache[sub.ChannelId] = new SubscriptionCacheEntry(
                sub.Title, sub.Thumbnail, details.Country, details.UploadsPlaylistId, Dead: false, playlistResult.Etag, sub.SubscriptionId);

            if (playlistResult.NotModified)
            {
                if (previousVideos.TryGetValue(sub.ChannelId, out var existing))
                    videosCache[sub.ChannelId] = existing;
                continue;
            }

            if (playlistResult.VideoIds.Count == 0) continue;
            videosCache[sub.ChannelId] = await _api.FetchVideosDetailsAsync(token, playlistResult.VideoIds);
        }

        _store.SaveSubscriptionsCache(subscriptionsCache);
        _store.SaveVideosCache(videosCache);
        _store.SetLastSyncedAt(DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter BackgroundSyncServiceTests`
Expected: Passed! 5 tests.

- [ ] **Step 5: Run the full test suite so far**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln`
Expected: Passed! All tests across every task so far.

- [ ] **Step 6: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Api/IYouTubeApiClient.cs
git add desktop-client/YouTubeDesktopClient/Api/YouTubeApiClient.cs
git add desktop-client/YouTubeDesktopClient/Sync
git add desktop-client/YouTubeDesktopClient.Tests/BackgroundSyncServiceTests.cs
git commit -m "feat: add ETag-aware background sync orchestration"
```

---

### Task 9: Feed view model

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Feed/FeedViewModel.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/FeedViewModelTests.cs`

**Interfaces:**
- Consumes: `SubscriptionStore` (Task 3), `VideoClassifier` (Task 2).
- Produces:
  - `record FeedItem(VideoInfo Video, string Type, int DurationSeconds, bool IsWatched)`
  - `class FeedViewModel(SubscriptionStore store)` with:
    - `string TypeFilter` (`"all" | "video" | "short" | "live"`), `string SortBy` (`"date" | "duration" | "views"`), `bool HideWatched` — plain settable properties.
    - `List<FeedItem> GetVisibleItems(string? activeGroupId)` — `null` means "all subscriptions".
    - `void MarkWatched(string videoId)` — delegates to the store, for the "mark watched" button.
- Consumed by: `FeedPanel.xaml.cs` (Task 12).

- [ ] **Step 1: Write the failing tests**

```csharp
// FeedViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class FeedViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;
    private readonly FeedViewModel _viewModel;

    public FeedViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
        _viewModel = new FeedViewModel(_store);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static VideoInfo MakeVideo(string id, string channelId, DateTimeOffset published, string duration, long views, string live = "none") =>
        new(id, channelId, $"Video {id}", null, published, duration, views, live);

    [Fact]
    public void GetVisibleItems_WithNullGroup_ReturnsAllChannels()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
            ["UC2"] = new() { MakeVideo("v2", "UC2", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        var items = _viewModel.GetVisibleItems(activeGroupId: null);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void GetVisibleItems_WithGroup_OnlyReturnsAssignedChannels()
    {
        _store.SaveGroups(new() { ["g1"] = new GroupData("Music", new List<string> { "UC1" }) });
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
            ["UC2"] = new() { MakeVideo("v2", "UC2", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        var items = _viewModel.GetVisibleItems("g1");

        Assert.Single(items);
        Assert.Equal("v1", items[0].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_FiltersByType()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new()
            {
                MakeVideo("short1", "UC1", DateTimeOffset.UtcNow, "PT30S", 10),
                MakeVideo("long1", "UC1", DateTimeOffset.UtcNow, "PT10M", 10),
            },
        });
        _viewModel.TypeFilter = "short";

        var items = _viewModel.GetVisibleItems(null);

        Assert.Single(items);
        Assert.Equal("short1", items[0].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_SortsByViewsDescending()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new()
            {
                MakeVideo("low", "UC1", DateTimeOffset.UtcNow, "PT2M", 5),
                MakeVideo("high", "UC1", DateTimeOffset.UtcNow, "PT2M", 500),
            },
        });
        _viewModel.SortBy = "views";

        var items = _viewModel.GetVisibleItems(null);

        Assert.Equal("high", items[0].Video.VideoId);
        Assert.Equal("low", items[1].Video.VideoId);
    }

    [Fact]
    public void GetVisibleItems_HidesWatched_WhenHideWatchedIsTrue()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
        });
        _store.MarkVideoWatched("v1");
        _viewModel.HideWatched = true;

        Assert.Empty(_viewModel.GetVisibleItems(null));
    }

    [Fact]
    public void MarkWatched_UpdatesStoreAndSubsequentQueries()
    {
        _store.SaveVideosCache(new()
        {
            ["UC1"] = new() { MakeVideo("v1", "UC1", DateTimeOffset.UtcNow, "PT2M", 10) },
        });

        _viewModel.MarkWatched("v1");

        Assert.True(_viewModel.GetVisibleItems(null)[0].IsWatched);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter FeedViewModelTests`
Expected: FAIL — `FeedViewModel` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Feed/FeedViewModel.cs
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Feed;

public record FeedItem(VideoInfo Video, string Type, int DurationSeconds, bool IsWatched);

public class FeedViewModel
{
    private readonly SubscriptionStore _store;

    public string TypeFilter { get; set; } = "all";
    public string SortBy { get; set; } = "date";
    public bool HideWatched { get; set; }

    public FeedViewModel(SubscriptionStore store) => _store = store;

    public List<FeedItem> GetVisibleItems(string? activeGroupId)
    {
        var videosCache = _store.GetVideosCache();
        var watchedIds = _store.GetWatchedVideoIds();

        IEnumerable<string> channelIds = activeGroupId == null
            ? videosCache.Keys
            : _store.GetGroups().TryGetValue(activeGroupId, out var group) ? group.ChannelIds : Enumerable.Empty<string>();

        var items = channelIds
            .Where(videosCache.ContainsKey)
            .SelectMany(channelId => videosCache[channelId])
            .Select(video =>
            {
                var durationSeconds = VideoClassifier.ParseIsoDuration(video.Duration);
                var type = VideoClassifier.ClassifyVideoType(video.LiveBroadcastContent, durationSeconds);
                return new FeedItem(video, type, durationSeconds, watchedIds.Contains(video.VideoId));
            });

        if (TypeFilter != "all")
            items = items.Where(i => i.Type == TypeFilter);
        if (HideWatched)
            items = items.Where(i => !i.IsWatched);

        items = SortBy switch
        {
            "duration" => items.OrderByDescending(i => i.DurationSeconds),
            "views" => items.OrderByDescending(i => i.Video.ViewCount),
            _ => items.OrderByDescending(i => i.Video.PublishedAt),
        };

        return items.ToList();
    }

    public void MarkWatched(string videoId) => _store.MarkVideoWatched(videoId);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter FeedViewModelTests`
Expected: Passed! 6 tests.

- [ ] **Step 5: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Feed/FeedViewModel.cs
git add desktop-client/YouTubeDesktopClient.Tests/FeedViewModelTests.cs
git commit -m "feat: add feed view model with group/type/sort/watched filtering"
```

---

### Task 10: Groups and channel management view models

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Groups/GroupsViewModel.cs`
- Create: `desktop-client/YouTubeDesktopClient/Channels/ChannelManagementViewModel.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/GroupsViewModelTests.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/ChannelManagementViewModelTests.cs`

**Interfaces:**
- `GroupsViewModel(SubscriptionStore store)`:
  - `Dictionary<string, GroupData> GetGroups()`
  - `string CreateGroup(string name)` — returns the new group id
  - `void RenameGroup(string groupId, string newName)`
  - `void DeleteGroup(string groupId)`
  - `void SetChannelInGroup(string groupId, string channelId, bool included)`
- `ChannelManagementViewModel(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)`:
  - `List<(string ChannelId, SubscriptionCacheEntry Entry)> GetDeadChannels()`
  - `Task UnsubscribeAsync(string channelId, string subscriptionId)` — calls the API then removes the channel from the cache.

- [ ] **Step 1: Write the failing tests**

```csharp
// GroupsViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Storage;

public class GroupsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GroupsViewModel _viewModel;

    public GroupsViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        var store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
        _viewModel = new GroupsViewModel(store);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void CreateGroup_AddsToGroupsList()
    {
        var id = _viewModel.CreateGroup("Music");

        Assert.Equal("Music", _viewModel.GetGroups()[id].Name);
        Assert.Empty(_viewModel.GetGroups()[id].ChannelIds);
    }

    [Fact]
    public void RenameGroup_UpdatesName()
    {
        var id = _viewModel.CreateGroup("Music");
        _viewModel.RenameGroup(id, "Podcasts");

        Assert.Equal("Podcasts", _viewModel.GetGroups()[id].Name);
    }

    [Fact]
    public void DeleteGroup_RemovesFromGroupsList()
    {
        var id = _viewModel.CreateGroup("Music");
        _viewModel.DeleteGroup(id);

        Assert.Empty(_viewModel.GetGroups());
    }

    [Fact]
    public void SetChannelInGroup_AddsThenRemovesChannel()
    {
        var id = _viewModel.CreateGroup("Music");

        _viewModel.SetChannelInGroup(id, "UC1", included: true);
        Assert.Contains("UC1", _viewModel.GetGroups()[id].ChannelIds);

        _viewModel.SetChannelInGroup(id, "UC1", included: false);
        Assert.DoesNotContain("UC1", _viewModel.GetGroups()[id].ChannelIds);
    }
}
```

```csharp
// ChannelManagementViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

public class ChannelManagementViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    public ChannelManagementViewModelTests()
    {
        _tempDir = Directory.CreateTempSubdirectory().FullName;
        _store = new SubscriptionStore(Path.Combine(_tempDir, "settings.json"), Path.Combine(_tempDir, "cache.json"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetDeadChannels_ReturnsOnlyChannelsMarkedDead()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC1"] = new SubscriptionCacheEntry("Alive", null, null, "UU1", Dead: false, null, "subAlive"),
            ["UC2"] = new SubscriptionCacheEntry("Dead One", null, null, null, Dead: true, null, "sub2"),
        });
        var viewModel = new ChannelManagementViewModel(new FakeYouTubeApiClient(), _store, () => Task.FromResult<string?>("token"));

        var dead = viewModel.GetDeadChannels();

        Assert.Single(dead);
        Assert.Equal("UC2", dead[0].ChannelId);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesChannelFromCache()
    {
        _store.SaveSubscriptionsCache(new()
        {
            ["UC2"] = new SubscriptionCacheEntry("Dead One", null, null, null, Dead: true, null, "sub2"),
        });
        var viewModel = new ChannelManagementViewModel(new FakeYouTubeApiClient(), _store, () => Task.FromResult<string?>("token"));

        await viewModel.UnsubscribeAsync("UC2", "sub2");

        Assert.False(_store.GetSubscriptionsCache().ContainsKey("UC2"));
    }
}
```

`FakeYouTubeApiClient` already exists from Task 8's test project — reused here as-is.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter "GroupsViewModelTests|ChannelManagementViewModelTests"`
Expected: FAIL — neither class exists yet.

- [ ] **Step 3: Implement GroupsViewModel**

```csharp
// Groups/GroupsViewModel.cs
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Groups;

public class GroupsViewModel
{
    private readonly SubscriptionStore _store;

    public GroupsViewModel(SubscriptionStore store) => _store = store;

    public Dictionary<string, GroupData> GetGroups() => _store.GetGroups();

    public string CreateGroup(string name)
    {
        var groups = _store.GetGroups();
        var id = $"g_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        groups[id] = new GroupData(name, new List<string>());
        _store.SaveGroups(groups);
        return id;
    }

    public void RenameGroup(string groupId, string newName)
    {
        var groups = _store.GetGroups();
        groups[groupId] = groups[groupId] with { Name = newName };
        _store.SaveGroups(groups);
    }

    public void DeleteGroup(string groupId)
    {
        var groups = _store.GetGroups();
        groups.Remove(groupId);
        _store.SaveGroups(groups);
    }

    public void SetChannelInGroup(string groupId, string channelId, bool included)
    {
        var groups = _store.GetGroups();
        var group = groups[groupId];
        var channelIds = new List<string>(group.ChannelIds);
        if (included && !channelIds.Contains(channelId)) channelIds.Add(channelId);
        if (!included) channelIds.Remove(channelId);
        groups[groupId] = group with { ChannelIds = channelIds };
        _store.SaveGroups(groups);
    }
}
```

- [ ] **Step 4: Implement ChannelManagementViewModel**

```csharp
// Channels/ChannelManagementViewModel.cs
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Channels;

public class ChannelManagementViewModel
{
    private readonly IYouTubeApiClient _api;
    private readonly SubscriptionStore _store;
    private readonly Func<Task<string?>> _getAccessToken;

    public ChannelManagementViewModel(IYouTubeApiClient api, SubscriptionStore store, Func<Task<string?>> getAccessToken)
    {
        _api = api;
        _store = store;
        _getAccessToken = getAccessToken;
    }

    public List<(string ChannelId, SubscriptionCacheEntry Entry)> GetDeadChannels() =>
        _store.GetSubscriptionsCache()
            .Where(kv => kv.Value.Dead)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public async Task UnsubscribeAsync(string channelId, string subscriptionId)
    {
        var token = await _getAccessToken() ?? throw new InvalidOperationException("Sign in first.");
        await _api.UnsubscribeAsync(token, subscriptionId);

        var cache = _store.GetSubscriptionsCache();
        cache.Remove(channelId);
        _store.SaveSubscriptionsCache(cache);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter "GroupsViewModelTests|ChannelManagementViewModelTests"`
Expected: Passed! 6 tests.

- [ ] **Step 6: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Groups desktop-client/YouTubeDesktopClient/Channels
git add desktop-client/YouTubeDesktopClient.Tests/GroupsViewModelTests.cs desktop-client/YouTubeDesktopClient.Tests/ChannelManagementViewModelTests.cs
git commit -m "feat: add groups and channel management view models"
```

---

### Task 11: Tab management logic (in-place vs. new tab)

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Tabs/TabsViewModel.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/TabsViewModelTests.cs`

**Interfaces:**
- Produces: `record TabInfo(string Id, string Title, string Url, bool IsPinned)` and `class TabsViewModel` with:
  - `List<TabInfo> Tabs` (starts with pinned "Feed" and "Home" tabs)
  - `string ActiveTabId`
  - `void NavigateActiveOrNewTab(string videoId, bool forceNewTab)` — if the active tab is a (non-pinned) video tab and `forceNewTab` is false, replaces its URL in place; otherwise appends a new tab and activates it.
  - `void CloseTab(string tabId)` — no-ops for pinned tabs.

This is the one piece of the tab-strip behavior that's pure decision logic (which tab gets the navigation) and worth testing in isolation, separate from the actual `WebView2` wiring in Task 12, which isn't unit-testable.

- [ ] **Step 1: Write the failing tests**

```csharp
// TabsViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Tabs;

public class TabsViewModelTests
{
    [Fact]
    public void InitialState_HasPinnedFeedAndHomeTabs()
    {
        var vm = new TabsViewModel();

        Assert.Equal(2, vm.Tabs.Count);
        Assert.All(vm.Tabs, t => Assert.True(t.IsPinned));
        Assert.Contains(vm.Tabs, t => t.Id == "feed");
        Assert.Contains(vm.Tabs, t => t.Id == "home");
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromFeedTab_AlwaysOpensNewTab()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };

        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);

        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal(vm.ActiveTabId, vm.Tabs[^1].Id);
        Assert.Contains("v1", vm.Tabs[^1].Url);
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromVideoTab_ReplacesInPlace_WhenNotForced()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);
        var videoTabId = vm.ActiveTabId;

        vm.NavigateActiveOrNewTab("v2", forceNewTab: false);

        Assert.Equal(3, vm.Tabs.Count); // no new tab added
        Assert.Equal(videoTabId, vm.ActiveTabId);
        Assert.Contains("v2", vm.Tabs.Single(t => t.Id == videoTabId).Url);
    }

    [Fact]
    public void NavigateActiveOrNewTab_FromVideoTab_OpensNewTab_WhenForced()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);

        vm.NavigateActiveOrNewTab("v2", forceNewTab: true);

        Assert.Equal(4, vm.Tabs.Count);
    }

    [Fact]
    public void CloseTab_RemovesNonPinnedTab()
    {
        var vm = new TabsViewModel { ActiveTabId = "feed" };
        vm.NavigateActiveOrNewTab("v1", forceNewTab: false);
        var videoTabId = vm.ActiveTabId;

        vm.CloseTab(videoTabId);

        Assert.DoesNotContain(vm.Tabs, t => t.Id == videoTabId);
    }

    [Fact]
    public void CloseTab_IgnoresPinnedTab()
    {
        var vm = new TabsViewModel();

        vm.CloseTab("feed");

        Assert.Contains(vm.Tabs, t => t.Id == "feed");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter TabsViewModelTests`
Expected: FAIL — `TabsViewModel` does not exist.

- [ ] **Step 3: Implement**

```csharp
// Tabs/TabsViewModel.cs
namespace YouTubeDesktopClient.Tabs;

public record TabInfo(string Id, string Title, string Url, bool IsPinned);

public class TabsViewModel
{
    private int _nextTabNumber = 1;
    public List<TabInfo> Tabs { get; } = new()
    {
        new TabInfo("feed", "Feed", "app://feed", IsPinned: true),
        new TabInfo("home", "Home", "https://www.youtube.com/", IsPinned: true),
    };

    public string ActiveTabId { get; set; } = "feed";

    public void NavigateActiveOrNewTab(string videoId, bool forceNewTab)
    {
        var url = $"https://www.youtube.com/watch?v={videoId}";
        var activeTab = Tabs.FirstOrDefault(t => t.Id == ActiveTabId);
        var activeIsReusableVideoTab = activeTab is { IsPinned: false };

        if (!forceNewTab && activeIsReusableVideoTab)
        {
            var index = Tabs.FindIndex(t => t.Id == ActiveTabId);
            Tabs[index] = activeTab! with { Url = url, Title = videoId };
            return;
        }

        var newTab = new TabInfo($"video{_nextTabNumber++}", videoId, url, IsPinned: false);
        Tabs.Add(newTab);
        ActiveTabId = newTab.Id;
    }

    public void CloseTab(string tabId)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null || tab.IsPinned) return;
        Tabs.Remove(tab);
        if (ActiveTabId == tabId) ActiveTabId = "feed";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter TabsViewModelTests`
Expected: Passed! 6 tests.

- [ ] **Step 5: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Tabs
git commit -m "feat: add tab management logic for in-place vs new-tab navigation"
```

---

### Task 12: WPF shell — windows, panels, and WebView2 wiring

**Files:**
- Create: `desktop-client/YouTubeDesktopClient/Views/GroupsPanel.xaml(.cs)`
- Create: `desktop-client/YouTubeDesktopClient/Views/FeedPanel.xaml(.cs)`
- Create: `desktop-client/YouTubeDesktopClient/Views/ChannelManagementPanel.xaml(.cs)`
- Modify: `desktop-client/YouTubeDesktopClient/MainWindow.xaml(.cs)`

**Interfaces:**
- Consumes: `GroupsViewModel`, `FeedViewModel`, `ChannelManagementViewModel`, `TabsViewModel` (all prior tasks).
- Produces: the running visual shell — no new testable logic (this task is UI wiring; the logic it calls is already covered by unit tests from Tasks 9–11).

This task is UI wiring, not unit-testable in the TDD sense — WPF control trees and `WebView2` navigation require a running window. Verify manually per the checklist at the end of the step.

- [ ] **Step 1: Build MainWindow's layout**

```xml
<!-- MainWindow.xaml -->
<Window x:Class="YouTubeDesktopClient.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="YouTube Desktop Client" Height="800" Width="1280"
        StateChanged="MainWindow_StateChanged" Closing="MainWindow_Closing">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="260"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <ContentControl x:Name="GroupsHost" Grid.Column="0"/>
        <TabControl x:Name="MainTabs" Grid.Column="1"
                    SelectionChanged="MainTabs_SelectionChanged"/>
    </Grid>
</Window>
```

- [ ] **Step 2: Wire MainWindow.xaml.cs to the view models and TabsViewModel**

```csharp
// MainWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Tabs;

namespace YouTubeDesktopClient;

public partial class MainWindow : Window
{
    private readonly TabsViewModel _tabs = new();
    private readonly Dictionary<string, WebView2> _webViewsByTabId = new();

    public MainWindow(GroupsViewModel groupsViewModel, FeedViewModel feedViewModel,
        ChannelManagementViewModel channelViewModel)
    {
        InitializeComponent();
        GroupsHost.Content = new Views.GroupsPanel(groupsViewModel, OnGroupSelected);
        RebuildTabStrip(feedViewModel, channelViewModel);
    }

    private void RebuildTabStrip(FeedViewModel feedViewModel, ChannelManagementViewModel channelViewModel)
    {
        MainTabs.Items.Clear();
        foreach (var tab in _tabs.Tabs)
        {
            var tabItem = new TabItem { Header = tab.Title, Tag = tab.Id };
            tabItem.Content = tab.Id switch
            {
                "feed" => new Views.FeedPanel(feedViewModel, videoId => OpenVideo(videoId, forceNewTab: false)),
                "home" => GetOrCreateWebView(tab.Id, tab.Url),
                _ => GetOrCreateWebView(tab.Id, tab.Url),
            };
            MainTabs.Items.Add(tabItem);
        }
    }

    private WebView2 GetOrCreateWebView(string tabId, string url)
    {
        if (_webViewsByTabId.TryGetValue(tabId, out var existing))
        {
            existing.Source = new Uri(url);
            return existing;
        }
        var webView = new WebView2();
        webView.EnsureCoreWebView2Async().ContinueWith(_ =>
            Dispatcher.Invoke(() => webView.Source = new Uri(url)));
        _webViewsByTabId[tabId] = webView;
        return webView;
    }

    private void OpenVideo(string videoId, bool forceNewTab)
    {
        _tabs.NavigateActiveOrNewTab(videoId, forceNewTab);
        RebuildTabStripPreservingFeedAndHome();
    }

    private void RebuildTabStripPreservingFeedAndHome()
    {
        // Re-render only the tab strip's items to match _tabs.Tabs; existing
        // WebView2 instances are reused via _webViewsByTabId so navigating
        // doesn't recreate the browser process for tabs that already exist.
        foreach (var tab in _tabs.Tabs.Where(t => !t.IsPinned))
        {
            if (MainTabs.Items.Cast<TabItem>().Any(ti => (string)ti.Tag == tab.Id)) continue;
            var tabItem = new TabItem { Header = tab.Title, Tag = tab.Id, Content = GetOrCreateWebView(tab.Id, tab.Url) };
            MainTabs.Items.Add(tabItem);
        }
        var activeItem = MainTabs.Items.Cast<TabItem>().FirstOrDefault(ti => (string)ti.Tag == _tabs.ActiveTabId);
        if (activeItem != null) MainTabs.SelectedItem = activeItem;

        // If the active video tab was reused in place, its WebView2 needs
        // the new URL explicitly, since GetOrCreateWebView above only runs
        // for *new* tabs.
        var activeTab = _tabs.Tabs.First(t => t.Id == _tabs.ActiveTabId);
        if (_webViewsByTabId.TryGetValue(activeTab.Id, out var webView))
            webView.Source = new Uri(activeTab.Url);
    }

    private void OnGroupSelected(string? groupId)
    {
        var feedTab = MainTabs.Items.Cast<TabItem>().First(ti => (string)ti.Tag == "feed");
        ((Views.FeedPanel)feedTab.Content).SetActiveGroup(groupId);
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedItem is TabItem { Tag: string tabId }) _tabs.ActiveTabId = tabId;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
```

- [ ] **Step 3: Build GroupsPanel**

```xml
<!-- Views/GroupsPanel.xaml -->
<UserControl x:Class="YouTubeDesktopClient.Views.GroupsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="8">
        <TextBlock Text="Groups" FontWeight="Bold" Margin="0,0,0,8"/>
        <ListBox x:Name="GroupList" SelectionChanged="GroupList_SelectionChanged"/>
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
            <TextBox x:Name="NewGroupName" Width="140"/>
            <Button Content="Add" Click="AddGroup_Click" Margin="4,0,0,0"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

```csharp
// Views/GroupsPanel.xaml.cs
using System.Windows.Controls;
using YouTubeDesktopClient.Groups;

namespace YouTubeDesktopClient.Views;

public partial class GroupsPanel : UserControl
{
    private readonly GroupsViewModel _viewModel;
    private readonly Action<string?> _onGroupSelected;

    public GroupsPanel(GroupsViewModel viewModel, Action<string?> onGroupSelected)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onGroupSelected = onGroupSelected;
        RefreshList();
    }

    private void RefreshList()
    {
        GroupList.Items.Clear();
        GroupList.Items.Add("All subscriptions");
        foreach (var (id, group) in _viewModel.GetGroups())
            GroupList.Items.Add(new ListBoxItem { Content = $"{group.Name} ({group.ChannelIds.Count})", Tag = id });
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var groupId = GroupList.SelectedItem is ListBoxItem { Tag: string id } ? id : null;
        _onGroupSelected(groupId);
    }

    private void AddGroup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewGroupName.Text)) return;
        _viewModel.CreateGroup(NewGroupName.Text);
        NewGroupName.Clear();
        RefreshList();
    }
}
```

- [ ] **Step 4: Build FeedPanel**

```xml
<!-- Views/FeedPanel.xaml -->
<UserControl x:Class="YouTubeDesktopClient.Views.FeedPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="8">
        <StackPanel Orientation="Horizontal" DockPanel.Dock="Top" Margin="0,0,0,8">
            <ComboBox x:Name="TypeFilterBox" Width="100" SelectionChanged="Filters_Changed">
                <ComboBoxItem Content="all" IsSelected="True"/>
                <ComboBoxItem Content="video"/>
                <ComboBoxItem Content="short"/>
                <ComboBoxItem Content="live"/>
            </ComboBox>
            <ComboBox x:Name="SortByBox" Width="100" Margin="8,0,0,0" SelectionChanged="Filters_Changed">
                <ComboBoxItem Content="date" IsSelected="True"/>
                <ComboBoxItem Content="duration"/>
                <ComboBoxItem Content="views"/>
            </ComboBox>
            <CheckBox x:Name="HideWatchedBox" Content="Hide watched" Margin="8,0,0,0" Checked="Filters_Changed" Unchecked="Filters_Changed"/>
        </StackPanel>
        <ItemsControl x:Name="VideoGrid">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
    </DockPanel>
</UserControl>
```

```csharp
// Views/FeedPanel.xaml.cs
using System.Windows;
using System.Windows.Controls;
using YouTubeDesktopClient.Feed;

namespace YouTubeDesktopClient.Views;

public partial class FeedPanel : UserControl
{
    private readonly FeedViewModel _viewModel;
    private readonly Action<string> _onVideoClicked;
    private string? _activeGroupId;

    public FeedPanel(FeedViewModel viewModel, Action<string> onVideoClicked)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onVideoClicked = onVideoClicked;
        RefreshFeed();
    }

    public void SetActiveGroup(string? groupId)
    {
        _activeGroupId = groupId;
        RefreshFeed();
    }

    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        _viewModel.TypeFilter = ((ComboBoxItem)TypeFilterBox.SelectedItem).Content.ToString()!;
        _viewModel.SortBy = ((ComboBoxItem)SortByBox.SelectedItem).Content.ToString()!;
        _viewModel.HideWatched = HideWatchedBox.IsChecked == true;
        RefreshFeed();
    }

    private void RefreshFeed()
    {
        VideoGrid.Items.Clear();
        foreach (var item in _viewModel.GetVisibleItems(_activeGroupId))
        {
            var card = new StackPanel { Width = 220, Margin = new Thickness(4) };
            card.Children.Add(new TextBlock { Text = item.Video.Title, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(new TextBlock { Text = $"{item.Type} · {item.Video.ViewCount} views" });
            var watchBtn = new Button { Content = item.IsWatched ? "Watched" : "Mark watched" };
            watchBtn.Click += (_, _) => { _viewModel.MarkWatched(item.Video.VideoId); RefreshFeed(); };
            card.Children.Add(watchBtn);
            var playBtn = new Button { Content = "Play" };
            playBtn.Click += (_, _) => _onVideoClicked(item.Video.VideoId);
            card.Children.Add(playBtn);
            VideoGrid.Items.Add(card);
        }
    }
}
```

- [ ] **Step 5: Build ChannelManagementPanel**

```xml
<!-- Views/ChannelManagementPanel.xaml -->
<UserControl x:Class="YouTubeDesktopClient.Views.ChannelManagementPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ListBox x:Name="DeadChannelsList" Margin="8"/>
</UserControl>
```

```csharp
// Views/ChannelManagementPanel.xaml.cs
using System.Windows.Controls;
using YouTubeDesktopClient.Channels;

namespace YouTubeDesktopClient.Views;

public partial class ChannelManagementPanel : UserControl
{
    private readonly ChannelManagementViewModel _viewModel;

    public ChannelManagementPanel(ChannelManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        Refresh();
    }

    private void Refresh()
    {
        DeadChannelsList.Items.Clear();
        foreach (var (channelId, entry) in _viewModel.GetDeadChannels())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = entry.Title, Width = 200 });
            var unsubBtn = new Button { Content = "Unsubscribe" };
            unsubBtn.Click += async (_, _) =>
            {
                await _viewModel.UnsubscribeAsync(channelId, entry.SubscriptionId!);
                Refresh();
            };
            row.Children.Add(unsubBtn);
            DeadChannelsList.Items.Add(row);
        }
    }
}
```

- [ ] **Step 6: Manual verification**

1. Run: `dotnet run --project desktop-client/YouTubeDesktopClient`
2. Confirm the window opens with an empty groups list, a "Feed" tab, and a "Home" tab showing the real youtube.com homepage.
3. Create a group, confirm it appears in the left panel.
4. Click "Play" on a feed card once real data exists (after Task 13 wires up sign-in) — confirm it opens a new tab with the real youtube.com watch page.
5. Click "Play" on another card while a video tab is active — confirm it navigates the *same* tab in place rather than opening a new one.

- [ ] **Step 7: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/Views desktop-client/YouTubeDesktopClient/MainWindow.xaml desktop-client/YouTubeDesktopClient/MainWindow.xaml.cs
git commit -m "feat: wire up WPF shell with groups panel, feed panel, and WebView2 tabs"
```

---

### Task 13: Sign-in wiring, tray icon, and startup registration

**Files:**
- Modify: `desktop-client/YouTubeDesktopClient/App.xaml.cs`
- Create: `desktop-client/YouTubeDesktopClient/Tray/TrayIconService.cs`
- Create: `desktop-client/YouTubeDesktopClient/Tray/StartupRegistration.cs`
- Test: `desktop-client/YouTubeDesktopClient.Tests/StartupRegistrationTests.cs`
- Modify: `desktop-client/YouTubeDesktopClient/YouTubeDesktopClient.csproj` (add `System.Windows.Forms` reference for `NotifyIcon`)

**Interfaces:**
- Produces: `static class StartupRegistration` with `bool IsEnabled()`, `void Enable()`, `void Disable()` (registry-backed, testable against an injectable registry key path); `class TrayIconService` (not unit-tested — thin wrapper around `System.Windows.Forms.NotifyIcon`).
- Wires together everything from Tasks 3–12 in `App.xaml.cs`'s startup path.

- [ ] **Step 1: Write the failing test for StartupRegistration**

```csharp
// StartupRegistrationTests.cs
using Microsoft.Win32;
using Xunit;
using YouTubeDesktopClient.Tray;

public class StartupRegistrationTests : IDisposable
{
    private const string TestValueName = "YouTubeDesktopClientTest";

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(TestValueName, throwOnMissingValue: false);
    }

    [Fact]
    public void Enable_ThenIsEnabled_ReturnsTrue()
    {
        StartupRegistration.Enable(TestValueName, @"C:\fake\path\app.exe");

        Assert.True(StartupRegistration.IsEnabled(TestValueName));
    }

    [Fact]
    public void Disable_RemovesRegistryValue()
    {
        StartupRegistration.Enable(TestValueName, @"C:\fake\path\app.exe");
        StartupRegistration.Disable(TestValueName);

        Assert.False(StartupRegistration.IsEnabled(TestValueName));
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenNeverSet()
    {
        Assert.False(StartupRegistration.IsEnabled(TestValueName));
    }
}
```

This test writes to the real `HKEY_CURRENT_USER\...\Run` key under a distinct test value name, and removes it in `Dispose` — acceptable for a personal desktop app's test suite (no admin rights required for `HKCU`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter StartupRegistrationTests`
Expected: FAIL — `StartupRegistration` does not exist.

- [ ] **Step 3: Implement StartupRegistration**

```csharp
// Tray/StartupRegistration.cs
using Microsoft.Win32;

namespace YouTubeDesktopClient.Tray;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(valueName) != null;
    }

    public static void Enable(string valueName, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(valueName, $"\"{executablePath}\"");
    }

    public static void Disable(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln --filter StartupRegistrationTests`
Expected: Passed! 3 tests.

- [ ] **Step 5: Add the Windows Forms reference for NotifyIcon**

```xml
<!-- YouTubeDesktopClient.csproj — add inside the existing <PropertyGroup> -->
<UseWindowsForms>true</UseWindowsForms>
```

- [ ] **Step 6: Implement TrayIconService**

```csharp
// Tray/TrayIconService.cs
using System.Windows.Forms;

namespace YouTubeDesktopClient.Tray;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(Action onOpen, Action onRefreshNow, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => onOpen());
        menu.Items.Add("Refresh now", null, (_, _) => onRefreshNow());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "YouTube Desktop Client",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => onOpen();
    }

    public void Dispose() => _notifyIcon.Dispose();
}
```

- [ ] **Step 7: Wire everything together in App.xaml.cs**

```csharp
// App.xaml.cs
using System.Windows;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Channels;
using YouTubeDesktopClient.Feed;
using YouTubeDesktopClient.Groups;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Sync;
using YouTubeDesktopClient.Tray;

namespace YouTubeDesktopClient;

public partial class App : Application
{
    private const string OAuthClientId = "PASTE_DESKTOP_OAUTH_CLIENT_ID_HERE.apps.googleusercontent.com";
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(3);

    private TrayIconService? _tray;
    private BackgroundSyncService? _sync;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YouTubeSubscriptionsToolkit");
        var store = new SubscriptionStore(
            Path.Combine(appDataDir, "settings.json"),
            Path.Combine(appDataDir, "cache.json"));
        var tokenStore = new TokenStore(Path.Combine(appDataDir, "token.bin"));
        var authService = new AuthService(OAuthClientId, tokenStore);
        var apiClient = new YouTubeApiClient(new HttpClient());

        Func<Task<string?>> getAccessToken = () => authService.GetAccessTokenSilentAsync();

        _sync = new BackgroundSyncService(apiClient, store, getAccessToken);
        _sync.Start(SyncInterval);

        var groupsViewModel = new GroupsViewModel(store);
        var feedViewModel = new FeedViewModel(store);
        var channelViewModel = new ChannelManagementViewModel(apiClient, store, getAccessToken);

        _mainWindow = new MainWindow(groupsViewModel, feedViewModel, channelViewModel);
        _mainWindow.Show();

        _tray = new TrayIconService(
            onOpen: () => { _mainWindow.Show(); _mainWindow.WindowState = WindowState.Normal; },
            onRefreshNow: () => _sync.RunOnceAsync().GetAwaiter().GetResult(),
            onExit: () => Shutdown());

        _ = SignInIfNeededAsync(authService);
    }

    private async Task SignInIfNeededAsync(AuthService authService)
    {
        var token = await authService.GetAccessTokenSilentAsync();
        if (token == null)
            await authService.SignInInteractiveAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sync?.Stop();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

Also remove the default `StartupUri` from `App.xaml` (the window is now created explicitly in `OnStartup`):

```xml
<!-- App.xaml — remove StartupUri="MainWindow.xaml" from the Application element -->
<Application x:Class="YouTubeDesktopClient.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</Application>
```

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test desktop-client/YouTubeDesktopClient.sln`
Expected: Passed! All tests from every task.

- [ ] **Step 9: Manual verification (full end-to-end)**

1. In Google Cloud Console, create a **Desktop app** OAuth client (same project as the extension's clients) and paste its client ID into `App.xaml.cs` in place of `PASTE_DESKTOP_OAUTH_CLIENT_ID_HERE...`.
2. Run: `dotnet run --project desktop-client/YouTubeDesktopClient`
3. Confirm the system browser opens for sign-in; grant access; confirm the app window shows real subscriptions in the Feed tab within one sync cycle (or click "Refresh now" from the tray icon to trigger it immediately).
4. Close the window (X button) — confirm the app minimizes to the tray rather than exiting, and the tray icon's context menu (Open / Refresh now / Exit) works.
5. Quit and relaunch the app — confirm it signs in silently (no browser popup) using the stored refresh token.
6. Create a group, assign a channel to it, confirm the feed filters correctly when that group is selected.
7. Click a video, confirm the real youtube.com watch page loads in a new tab with comments/related videos intact; click another video from the feed while that tab is active and confirm it navigates in place; verify an explicit "open in new tab" path also works if wired to a middle-click or a dedicated button.
8. Check the Channel Management panel after temporarily forcing a channel into the dead state (e.g. by editing `cache.json`'s `Dead` field by hand) and confirm "Unsubscribe" removes it.

- [ ] **Step 10: Commit**

```bash
git add desktop-client/YouTubeDesktopClient/App.xaml desktop-client/YouTubeDesktopClient/App.xaml.cs
git add desktop-client/YouTubeDesktopClient/Tray
git add desktop-client/YouTubeDesktopClient/YouTubeDesktopClient.csproj
git add desktop-client/YouTubeDesktopClient.Tests/StartupRegistrationTests.cs
git commit -m "feat: wire sign-in, background sync, and tray icon into app startup"
```

---

## Spec coverage check

- Stack (.NET 10, WPF, WebView2) — Task 1.
- JSON storage split (settings/cache) — Task 3.
- Auth (Desktop OAuth, PKCE, loopback, DPAPI token storage, silent refresh) — Tasks 4, 5, 7.
- API client + ETag quota strategy — Tasks 6, 8.
- Groups, feed filter/sort, watched — Tasks 9, 10.
- Dead-channel detection + unsubscribe — Tasks 10, 12.
- Tabs (pinned Feed/Home, in-place vs. new-tab video navigation) — Tasks 11, 12.
- Tray, background timer, start-with-Windows — Task 13.
- "What's real vs. local-only" (mark-watched is local; real playback writes real history; no hide/not-interested API) — reflected directly in the Feed/tab design (Tasks 9, 12); no separate task needed since it's a documentation point about existing behavior, not new code.
