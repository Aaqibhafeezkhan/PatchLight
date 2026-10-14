using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// The things only an administrator can do. The app runs them in a second copy of itself
// started with "Run as administrator".
public interface IElevatedActions
{
    Task HoldAsync(string kb, int days, CancellationToken ct);
    Task ReleaseAsync(string kb, CancellationToken ct);
    Task EnableDailyCheckAsync(CancellationToken ct);
    Task DisableDailyCheckAsync(CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class ElevatedActions(PatchLightService service, string exePath) : IElevatedActions
{
    public Task HoldAsync(string kb, int days, CancellationToken ct) => service.HoldAsync(kb, days, "Paused by you", ct);

    public Task ReleaseAsync(string kb, CancellationToken ct) => service.ReleaseAsync(kb, ct);

    public Task EnableDailyCheckAsync(CancellationToken ct) => Task.Run(() => DailyTask.Register(exePath), ct);

    public Task DisableDailyCheckAsync(CancellationToken ct) => Task.Run(DailyTask.Unregister, ct);
}

// Command line of the elevated copy: "--hold KB5127648 --days 14 --result <file>" and friends.
// The result file is left empty on success, or holds one plain sentence saying what went wrong.
public sealed record ElevatedCommand(string Verb, string? Kb, int Days, string ResultPath)
{
    private const string Hold = "--hold";
    private const string Release = "--release";
    private const string Enable = "--enable-daily-check";
    private const string Disable = "--disable-daily-check";

    public static IReadOnlyList<string> HoldArgs(string kb, int days) => [Hold, kb, "--days", days.ToString()];
    public static IReadOnlyList<string> ReleaseArgs(string kb) => [Release, kb];
    public static IReadOnlyList<string> EnableDailyCheckArgs { get; } = [Enable];
    public static IReadOnlyList<string> DisableDailyCheckArgs { get; } = [Disable];

    public static ElevatedCommand? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not (Hold or Release or Enable or Disable)) return null;

        var resultPath = ValueAfter(args, "--result");
        if (resultPath is null) return null;

        string? kb = null;
        if (args[0] is Hold or Release)
        {
            kb = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
            if (kb is null) return null;
        }

        var days = HoldPolicy.DefaultDays;
        if (ValueAfter(args, "--days") is { } daysText)
        {
            if (!int.TryParse(daysText, out days)) return null;
            days = Math.Clamp(days, 1, HoldPolicy.MaxDays);
        }
        return new ElevatedCommand(args[0], kb, days, resultPath);
    }

    public async Task<int> RunAsync(IElevatedActions actions, CancellationToken ct)
    {
        string message;
        try
        {
            await (Verb switch
            {
                Hold => actions.HoldAsync(Kb!, Days, ct),
                Release => actions.ReleaseAsync(Kb!, ct),
                Enable => actions.EnableDailyCheckAsync(ct),
                _ => actions.DisableDailyCheckAsync(ct),
            });
            message = "";
        }
        catch (PatchLightException ex)
        {
            message = ex.Message;
        }
        catch (Exception)
        {
            message = "Something went wrong.";
        }

        await File.WriteAllTextAsync(ResultPath, message, CancellationToken.None);
        return message.Length == 0 ? 0 : 2;
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
