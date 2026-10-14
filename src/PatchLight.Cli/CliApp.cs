using PatchLight.Core;
using PatchLight.Windows;
using Spectre.Console;

namespace PatchLight.Cli;

public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var console = AnsiConsole.Console;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var command = args.Length == 0 ? "check" : args[0].ToLowerInvariant();
            if (command is "help" or "--help" or "-h" or "/?")
            {
                Render.Help(console);
                return 0;
            }

            var windows = SystemInfo.Read();
            if (!windows.IsWindows11 || windows.DisplayVersion is null)
                throw new PatchLightException("PatchLight works on Windows 11 only for now.");

            var isAdmin = SystemInfo.IsAdministrator();
            // The daily check runs as SYSTEM and trusts this folder, so lock it whenever we can.
            if (isAdmin) DataFolder.Secure(SystemInfo.DataDirectory);
            var service = PatchLightHost.CreateService(windows.DisplayVersion);

            switch (command)
            {
                case "check":
                    var result = await console.Status().StartAsync(
                        "Checking Windows Update and Microsoft's known issues...",
                        _ => service.CheckAsync(applyAutoRelease: isAdmin, cts.Token));
                    Render.Check(console, result, isAdmin);
                    return result.Verdicts.Any(v => v.Light == Light.Red && !v.Update.IsHidden) ? 1 : 0;

                case "hold":
                    var kbToHold = CliArgs.Required(args, 1, "hold KB5127648");
                    var days = CliArgs.Days(args, HoldPolicy.DefaultDays);
                    RequireAdmin(isAdmin);
                    var hold = await console.Status().StartAsync("Pausing...",
                        _ => service.HoldAsync(kbToHold, days, "Paused by you", cts.Token));
                    Render.Held(console, hold);
                    return 0;

                case "release":
                    var kbToRelease = CliArgs.Required(args, 1, "release KB5127648");
                    RequireAdmin(isAdmin);
                    var released = await console.Status().StartAsync("Resuming...",
                        _ => service.ReleaseAsync(kbToRelease, cts.Token));
                    Render.Released(console, released);
                    return 0;

                case "history":
                    var history = await console.Status().StartAsync("Reading update history...",
                        _ => service.HistoryAsync(CliArgs.Days(args, 60), cts.Token));
                    Render.History(console, history);
                    return 0;

                default:
                    console.MarkupLineInterpolated($"[red]Unknown command '{command}'.[/]");
                    Render.Help(console);
                    return 2;
            }
        }
        catch (PatchLightException ex)
        {
            console.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return 2;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            var log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight", "last-error.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.WriteAllText(log, ex.ToString());
            }
            catch (Exception)
            {
            }
            console.MarkupLineInterpolated($"[red]Something went wrong. Details were saved to {log}.[/]");
            return 2;
        }
    }

    private static void RequireAdmin(bool isAdmin)
    {
        if (!isAdmin)
            throw new PatchLightException(
                "Pausing and resuming need administrator rights. Open Terminal as administrator and run this again.");
    }
}
