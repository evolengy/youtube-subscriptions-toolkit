// AccountViewModelTests.cs
using Xunit;
using YouTubeDesktopClient.Account;
using YouTubeDesktopClient.Api;
using YouTubeDesktopClient.Auth;
using YouTubeDesktopClient.Storage;
using YouTubeDesktopClient.Storage.Models;

namespace YouTubeDesktopClient.Tests;

file sealed class FakeAuth : IAuthService
{
    public string? SilentToken { get; set; }
    public string InteractiveToken { get; set; } = "interactive-token";
    public bool SignedOutCalled { get; private set; }

    public Task<string?> GetAccessTokenSilentAsync() => Task.FromResult(SilentToken);
    public Task<string> SignInInteractiveAsync(CancellationToken ct = default) => Task.FromResult(InteractiveToken);
    public void SignOut() { SignedOutCalled = true; SilentToken = null; }
}

file sealed class FakeAccountApi : IYouTubeAccountApi
{
    public MyChannel? Channel { get; set; } = new("UC_A", "Account A", null, "@a");
    public Task<MyChannel?> GetMyChannelAsync(string accessToken) => Task.FromResult(Channel);
}

public class AccountViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private readonly SubscriptionStore _store;
    private int _wipes;
    private int _relaunches;

    public AccountViewModelTests() =>
        _store = new SubscriptionStore(Path.Combine(_dir, "settings.json"), Path.Combine(_dir, "cache.json"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AccountViewModel Make(IAuthService auth, IYouTubeAccountApi api) =>
        new(auth, api, _store, _dir, wipeWebViewProfile: () => _wipes++, relaunch: () => _relaunches++);

    [Fact]
    public async Task ResolveAsync_WithTokenAndChannel_SignsInAndActivatesTheStore()
    {
        var vm = Make(new FakeAuth { SilentToken = "t" }, new FakeAccountApi());

        Assert.True(await vm.ResolveAsync());
        Assert.True(vm.IsSignedIn);
        Assert.Equal("Account A", vm.Channel!.Title);

        _store.SaveGroups(new() { ["g"] = new GroupData("A only", new()) });
        Assert.True(File.Exists(Path.Combine(_dir, "accounts", "UC_A", "settings.json")));
    }

    [Fact]
    public async Task ResolveAsync_NoToken_StaysSignedOut()
    {
        var vm = Make(new FakeAuth { SilentToken = null }, new FakeAccountApi());

        Assert.False(await vm.ResolveAsync());
        Assert.False(vm.IsSignedIn);
    }

    [Fact]
    public async Task SignOutAsync_ClearsTokenWipesProfileAndEmptiesTheStore()
    {
        var auth = new FakeAuth { SilentToken = "t" };
        var vm = Make(auth, new FakeAccountApi());
        await vm.ResolveAsync();
        _store.SaveGroups(new() { ["g"] = new GroupData("A", new()) });

        await vm.SignOutAsync();

        Assert.False(vm.IsSignedIn);
        Assert.True(auth.SignedOutCalled);
        Assert.Equal(1, _wipes);
        Assert.Empty(_store.GetGroups());
    }

    [Fact]
    public async Task SignInAsync_FromSignedOut_SignsInWithoutRelaunch()
    {
        var vm = Make(new FakeAuth(), new FakeAccountApi());

        await vm.SignInAsync();

        Assert.True(vm.IsSignedIn);
        Assert.Equal(0, _relaunches);
    }

    [Fact]
    public async Task SignInAsync_SwitchingAccounts_Relaunches()
    {
        var auth = new FakeAuth { SilentToken = "t" };
        var api = new FakeAccountApi { Channel = new("UC_A", "Account A", null, "@a") };
        var vm = Make(auth, api);
        await vm.ResolveAsync();

        api.Channel = new("UC_B", "Account B", null, "@b");
        await vm.SignInAsync();

        Assert.Equal(1, _relaunches);
        Assert.Equal("UC_A", vm.Channel!.ChannelId); // unchanged; the relaunch does the switch
    }

    [Fact]
    public async Task SignIn_ThenBackToFirstAccount_RestoresItsGroups()
    {
        var auth = new FakeAuth { SilentToken = "t" };
        var api = new FakeAccountApi { Channel = new("UC_A", "A", null, null) };
        var vm = Make(auth, api);

        await vm.ResolveAsync();
        _store.SaveGroups(new() { ["g1"] = new GroupData("A's", new() { "x" }) });

        // Simulate a relaunch into account B, then back into A.
        _store.ActivateAccount("UC_B", _dir);
        Assert.Empty(_store.GetGroups());
        _store.ActivateAccount("UC_A", _dir);

        Assert.Equal("A's", _store.GetGroups()["g1"].Name);
    }
}
