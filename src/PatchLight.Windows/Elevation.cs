using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

[SupportedOSPlatform("windows")]
public static class Elevation
{
    private const int UserCancelled = 1223;

    // Starts this program again as administrator (Windows shows its permission prompt) and waits for it.
    public static async Task RunSelfElevatedAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new PatchLightException("Couldn't find PatchLight's own program file.");
        var resultPath = Path.Combine(Path.GetTempPath(), $"patchlight-result-{Guid.NewGuid():N}.txt");
        var start = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--result");
        start.ArgumentList.Add(resultPath);

        try
        {
            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UserCancelled)
            {
                throw new PatchLightException("Windows asked for permission and it wasn't given, so nothing changed.");
            }
            if (process is null) throw new PatchLightException("Something went wrong.");

            using (process)
                await process.WaitForExitAsync(ct);

            var message = File.Exists(resultPath) ? (await File.ReadAllTextAsync(resultPath, ct)).Trim() : null;
            if (!string.IsNullOrEmpty(message)) throw new PatchLightException(message);
            if (message is null) throw new PatchLightException("Something went wrong.");
        }
        finally
        {
            File.Delete(resultPath);
        }
    }
}
