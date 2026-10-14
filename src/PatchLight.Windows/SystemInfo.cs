using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PatchLight.Core;

namespace PatchLight.Windows;

public sealed record WindowsInfo(string? DisplayVersion, Version Build, bool IsWindows11);

[SupportedOSPlatform("windows")]
public static class SystemInfo
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PatchLight");

    // ProductName says "Windows 10" on Windows 11, so go by the build number.
    public static WindowsInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new PatchLightException("Couldn't read the Windows version on this PC.");
        var build = int.Parse((string)key.GetValue("CurrentBuild", "0")!);
        var ubr = Convert.ToInt32(key.GetValue("UBR", 0));
        return new WindowsInfo(key.GetValue("DisplayVersion") as string, new Version(build, ubr), build >= 22000);
    }

    // Joined to a company domain or to Microsoft Entra (work or school account set up as the PC's owner).
    // Such PCs see every known issue; others set aside issues that usually only hit business PCs.
    public static bool IsWorkPc()
    {
        const int NetSetupDomainName = 3;
        if (NetGetJoinInformation(null, out var name, out var status) == 0)
        {
            NetApiBufferFree(name);
            if (status == NetSetupDomainName) return true;
        }
        using var entra = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
        return entra?.SubKeyCount > 0;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr name, out int status);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
