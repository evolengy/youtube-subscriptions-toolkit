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
