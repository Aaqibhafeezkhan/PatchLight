using System.Diagnostics;
using System.Runtime.Versioning;

namespace PatchLight.Windows;

// Settings > Windows Update shows the list from its last search, so a paused update would still show
// there for hours. Asking Windows for a fresh search makes the page match what PatchLight just did.
[SupportedOSPlatform("windows")]
public static class WindowsUpdateScan
{
    public static void Start()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "UsoClient.exe"), "StartScan")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Only cosmetic: Windows searches again on its own later.
        }
    }
}
