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
    // Returns a warning to show, or throws with a plain message.
    public static async Task<string?> RunSelfElevatedAsync(IReadOnlyList<string> args, CancellationToken ct)
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

            int exitCode;
            using (process)
            {
                await process.WaitForExitAsync(ct);
                exitCode = process.ExitCode;
            }
            return ReadResult(File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath, ct) : null, exitCode);
        }
        finally
        {
            File.Delete(resultPath);
        }
    }

    // Empty = done; "warning:..." = done, with something to tell the person; anything else = what went wrong.
    public static string? ReadResult(string? content, int exitCode)
    {
        if (content is null) throw new PatchLightException("Something went wrong.");
        var message = content.Trim();
        if (message.StartsWith(ElevatedCommand.WarningPrefix, StringComparison.Ordinal))
            return message[ElevatedCommand.WarningPrefix.Length..].Trim();
        if (message.Length > 0) throw new PatchLightException(message);
        return exitCode == 0 ? null : throw new PatchLightException("Something went wrong.");
    }
}

// What the app (running as a normal user) uses: each action becomes one Windows permission prompt.
[SupportedOSPlatform("windows")]
public sealed class RunAsAdministrator : IElevatedActions
{
    public Task<string?> HoldAsync(string kb, int days, CancellationToken ct) =>
        Elevation.RunSelfElevatedAsync(ElevatedCommand.HoldArgs(kb, days), ct);

    public Task ReleaseAsync(string kb, CancellationToken ct) =>
        Elevation.RunSelfElevatedAsync(ElevatedCommand.ReleaseArgs(kb), ct);

    public Task EnableDailyCheckAsync(CancellationToken ct) =>
        Elevation.RunSelfElevatedAsync(ElevatedCommand.EnableDailyCheckArgs, ct);

    public Task DisableDailyCheckAsync(CancellationToken ct) =>
        Elevation.RunSelfElevatedAsync(ElevatedCommand.DisableDailyCheckArgs, ct);
}
