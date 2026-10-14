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

            var service = CreateService(windows.DisplayVersion);
            var isAdmin = SystemInfo.IsAdministrator();

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
    }

    private static PatchLightService CreateService(string windowsVersion)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PatchLight/0.1 (+https://github.com/)");
        var source = new ReleaseHealthSource(http, Path.Combine(SystemInfo.DataDirectory, "cache"), TimeProvider.System);
        var holds = new HoldStore(Path.Combine(SystemInfo.DataDirectory, "holds.json"));
        return new PatchLightService(new WindowsUpdateClient(), source, holds, TimeProvider.System, windowsVersion);
    }

    private static void RequireAdmin(bool isAdmin)
    {
        if (!isAdmin)
            throw new PatchLightException(
                "Pausing and resuming need administrator rights. Open Terminal as administrator and run this again.");
    }
}
