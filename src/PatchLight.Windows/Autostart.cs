using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PatchLight.Windows;

// "Start with Windows": a per-user Run entry that opens PatchLight quietly in the tray.
[SupportedOSPlatform("windows")]
public sealed class Autostart(RegistryKey runKey)
{
    private const string ValueName = "PatchLight";

    public static Autostart ForCurrentUser() =>
        new(Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"));

    public bool HasEntry => runKey.GetValue(ValueName) is not null;

    // An entry that points at an older copy of the app doesn't count.
    public bool IsEnabled(string exePath) =>
        string.Equals(runKey.GetValue(ValueName) as string, Command(exePath), StringComparison.OrdinalIgnoreCase);

    public void Enable(string exePath) => runKey.SetValue(ValueName, Command(exePath));

    public void Disable() => runKey.DeleteValue(ValueName, throwOnMissingValue: false);

    private static string Command(string exePath) => $"\"{exePath}\" --tray";
}
