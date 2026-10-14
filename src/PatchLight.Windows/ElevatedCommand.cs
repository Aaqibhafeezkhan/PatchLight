using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// The things only an administrator can do. The app runs them in a second copy of itself
// started with "Run as administrator".
public interface IElevatedActions
{
    // Returns a warning when the pause worked but something around it didn't.
    Task<string?> HoldAsync(string kb, int days, CancellationToken ct);
    Task ReleaseAsync(string kb, CancellationToken ct);

    // Removes an installed update and keeps it paused when Windows offers it again. Returns a warning, or null.
    Task<string?> UninstallAsync(string kb, int days, CancellationToken ct);
    Task EnableDailyCheckAsync(CancellationToken ct);
    Task DisableDailyCheckAsync(CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class ElevatedActions(PatchLightService service, string exePath) : IElevatedActions
{
    // A pause must always end, so pausing also turns on the daily check that ends it.
    public async Task<string?> HoldAsync(string kb, int days, CancellationToken ct)
    {
        var hold = await service.HoldAsync(kb, days, "Paused by you", ct);
        return EnsureDailyCheck(hold, $"Paused {hold.Kb}");
    }

    public async Task<string?> UninstallAsync(string kb, int days, CancellationToken ct)
    {
        var installed = await service.GetInstalledAsync(kb, ct);
        IReadOnlyList<string> issueIds = [];
        try
        {
            issueIds = (await service.HistoryAsync(3650, ct)).FirstOrDefault(h => h.Update.Kb == installed.Kb)?
                .Issues.Select(i => i.Id).ToList() ?? [];
        }
        catch (PatchLightException)
        {
            // Without Microsoft's list the pause still ends on time; it just can't end early when fixed.
        }

        await Task.Run(() => UpdateRemover.Remove(installed), ct);
        var hold = await service.PlanHoldAsync(installed.Kb!, days, issueIds, ct);
        return EnsureDailyCheck(hold, $"Removed {hold.Kb}");
    }

    // A pause must always end, and a removed update must be paused when it comes back: both need the daily check.
    private string? EnsureDailyCheck(Hold hold, string done)
    {
        try
        {
            var registered = DailyTask.RegisteredExePath();
            if (!string.Equals(registered, exePath, StringComparison.OrdinalIgnoreCase)) DailyTask.Register(exePath);
            return null;
        }
        catch (PatchLightException ex)
        {
            return hold.IsWaiting
                ? $"{done}, but the daily check couldn't be turned on, so Windows may install it again. {ex.Message}"
                : $"{done}, but the daily check that ends pauses couldn't be turned on. {ex.Message} " +
                  $"Until then, resume {hold.Kb} yourself by {hold.ExpiresOn.ToLocalTime():MMM d}.";
        }
    }

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
    private const string Uninstall = "--uninstall-update";
    public const string WarningPrefix = "warning:";

    public static IReadOnlyList<string> HoldArgs(string kb, int days) => [Hold, kb, "--days", days.ToString()];
    public static IReadOnlyList<string> ReleaseArgs(string kb) => [Release, kb];
    public static IReadOnlyList<string> UninstallArgs(string kb, int days) => [Uninstall, kb, "--days", days.ToString()];
    public static IReadOnlyList<string> EnableDailyCheckArgs { get; } = [Enable];
    public static IReadOnlyList<string> DisableDailyCheckArgs { get; } = [Disable];

    public static ElevatedCommand? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not (Hold or Release or Enable or Disable or Uninstall)) return null;

        var resultPath = ValueAfter(args, "--result");
        if (resultPath is null) return null;

        string? kb = null;
        if (args[0] is Hold or Release or Uninstall)
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
            string? warning = null;
            switch (Verb)
            {
                case Hold: warning = await actions.HoldAsync(Kb!, Days, ct); break;
                case Release: await actions.ReleaseAsync(Kb!, ct); break;
                case Uninstall: warning = await actions.UninstallAsync(Kb!, Days, ct); break;
                case Enable: await actions.EnableDailyCheckAsync(ct); break;
                default: await actions.DisableDailyCheckAsync(ct); break;
            }
            message = warning is null ? "" : WarningPrefix + warning;
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
        return message.Length == 0 || message.StartsWith(WarningPrefix, StringComparison.Ordinal) ? 0 : 2;
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
