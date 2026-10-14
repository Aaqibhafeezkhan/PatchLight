using System.Runtime.Versioning;
using Microsoft.Win32;
using PatchLight.Core;

namespace PatchLight.Windows;

// DWORD values under one registry key; a seam so the policy logic can be tested without the registry.
public interface IPolicyValues
{
    int? Get(string name);
    void Set(string name, int value);
    void Delete(string name);
}

// "Wait before installing new updates" on Pro, Enterprise and Education: Windows' own deferral policy, so Windows
// doesn't even offer a monthly update until it is that many days old. PatchLight only ever changes values it wrote.
public static class DeferPolicy
{
    public const string Enabled = "DeferQualityUpdates";
    public const string Period = "DeferQualityUpdatesPeriodInDays";

    public static bool IsManagedElsewhere(IPolicyValues values, bool owns) =>
        !owns && (values.Get(Enabled) is not null || values.Get(Period) is not null);

    // Returns whether PatchLight owns the policy afterwards.
    public static bool Apply(IPolicyValues values, int days, bool owns)
    {
        if (IsManagedElsewhere(values, owns))
        {
            if (days == 0) return false;
            throw new PatchLightException(
                "Windows update waiting is managed by your organization on this PC, so PatchLight leaves it alone.");
        }
        if (days == 0)
        {
            if (owns)
            {
                values.Delete(Period);
                values.Delete(Enabled);
            }
            return false;
        }
        values.Set(Enabled, 1);
        values.Set(Period, days);
        return true;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsUpdatePolicyValues : IPolicyValues
{
    private const string KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public int? Get(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value ? value : null;
    }

    public void Set(string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void Delete(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
