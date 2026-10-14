using Microsoft.Win32;
using PatchLight.Windows;

namespace PatchLight.Tests;

public sealed class AutostartTests : IDisposable
{
    private readonly string keyPath = @"Software\PatchLightTests-" + Guid.NewGuid();
    private readonly RegistryKey key;

    public AutostartTests() => key = Registry.CurrentUser.CreateSubKey(keyPath);

    public void Dispose()
    {
        key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Enable_then_disable()
    {
        var autostart = new Autostart(key);
        const string exe = @"C:\Apps\PatchLight\PatchLight.exe";
        Assert.False(autostart.IsEnabled(exe));

        autostart.Enable(exe);
        Assert.True(autostart.IsEnabled(exe));
        Assert.Equal("\"C:\\Apps\\PatchLight\\PatchLight.exe\" --tray", key.GetValue("PatchLight"));

        autostart.Disable();
        Assert.False(autostart.IsEnabled(exe));
        Assert.Null(key.GetValue("PatchLight"));
    }

    [Fact]
    public void Entry_for_a_moved_app_does_not_count()
    {
        var autostart = new Autostart(key);
        autostart.Enable(@"C:\Old\PatchLight.exe");
        Assert.False(autostart.IsEnabled(@"C:\New\PatchLight.exe"));
        Assert.True(autostart.HasEntry);
    }

    [Fact]
    public void Disabling_when_off_is_fine() => new Autostart(key).Disable();
}
