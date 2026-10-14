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

    // Saves what the background check does on its own. Returns a warning, or null.
    Task<string?> SetProtectionAsync(ProtectionChoice choice, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class ElevatedActions(
    PatchLightService service,
    string exePath,
    ProtectionStore? protection = null,
    IPolicyValues? policy = null,
    bool isHome = false) : IElevatedActions
{
    // A pause must always end, so pausing also turns on the automatic check that ends it.
    public async Task<string?> HoldAsync(string kb, int days, CancellationToken ct)
    {
        var hold = await service.HoldAsync(kb, days, "Paused by you", ct);
        WindowsUpdateScan.Start();
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

    // A pause must always end, and a removed update must be paused when it comes back: both need the automatic check.
    private string? EnsureDailyCheck(Hold hold, string done)
    {
        try
        {
            if (DailyTask.NeedsRegister(exePath)) DailyTask.Register(exePath);
            return null;
        }
        catch (PatchLightException ex)
        {
            return hold.IsWaiting
                ? $"{done}, but the automatic check couldn't be turned on, so Windows may install it again. {ex.Message}"
                : $"{done}, but the automatic check that ends pauses couldn't be turned on. {ex.Message} " +
                  $"Until then, resume {hold.Kb} yourself by {hold.ExpiresOn.ToLocalTime():MMM d}.";
        }
    }

    // Resuming by hand means "let it through": automatic pausing leaves it alone until it gets a new problem.
    public async Task ReleaseAsync(string kb, CancellationToken ct)
    {
        var released = await service.ReleaseAsync(kb, ct);
        WindowsUpdateScan.Start();
        if (protection is null) return;
        try
        {
            protection.Save(protection.Load().WithLetThrough(released.Hold.Kb, released.Hold.IssueIds));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Worst case, auto-pause pauses it again on the next check.
        }
    }

    public async Task<string?> SetProtectionAsync(ProtectionChoice choice, CancellationToken ct)
    {
        var store = protection ?? throw new PatchLightException("Something went wrong.");
        var current = store.Load();
        var waitDays = new ProtectionSettings(WaitDays: choice.WaitDays).WaitDays;

        // Home ignores Windows' deferral policy; there the hourly check does the waiting with pauses.
        var owns = current.OwnsDeferPolicy;
        if (!isHome && policy is not null) owns = DeferPolicy.Apply(policy, waitDays, owns);

        store.Save(current with { AutoPauseRed = choice.AutoPauseRed, WaitDays = waitDays, PauseDays = choice.PauseDays, OwnsDeferPolicy = owns });
        WindowsUpdateScan.Start();

        if (!choice.AutoPauseRed && waitDays == 0) return null;
        try
        {
            await Task.Run(() =>
            {
                if (DailyTask.NeedsRegister(exePath)) DailyTask.Register(exePath);
            }, ct);
            return null;
        }
        catch (PatchLightException ex)
        {
            return $"Saved, but the automatic check that does this couldn't be turned on. {ex.Message}";
        }
    }

    public Task EnableDailyCheckAsync(CancellationToken ct) => Task.Run(() => DailyTask.Register(exePath), ct);

    public Task DisableDailyCheckAsync(CancellationToken ct) => Task.Run(DailyTask.Unregister, ct);
}

// Command line of the elevated copy: "--hold KB5127648 --days 14 --result <file>" and friends.
// The result file is left empty on success, or holds one plain sentence saying what went wrong.
public sealed record ElevatedCommand(string Verb, string? Kb, int Days, string ResultPath, ProtectionChoice? Protection = null)
{
    private const string Hold = "--hold";
    private const string Release = "--release";
    private const string Enable = "--enable-daily-check";
    private const string Disable = "--disable-daily-check";
    private const string Uninstall = "--uninstall-update";
    private const string Protect = "--set-protection";
    public const string WarningPrefix = "warning:";

    public static IReadOnlyList<string> HoldArgs(string kb, int days) => [Hold, kb, "--days", days.ToString()];
    public static IReadOnlyList<string> ReleaseArgs(string kb) => [Release, kb];
    public static IReadOnlyList<string> UninstallArgs(string kb, int days) => [Uninstall, kb, "--days", days.ToString()];
    public static IReadOnlyList<string> EnableDailyCheckArgs { get; } = [Enable];
    public static IReadOnlyList<string> DisableDailyCheckArgs { get; } = [Disable];

    public static IReadOnlyList<string> SetProtectionArgs(ProtectionChoice choice) =>
        [Protect, "--auto-pause", choice.AutoPauseRed ? "on" : "off", "--wait-days", choice.WaitDays.ToString(),
            "--days", choice.PauseDays.ToString()];

    public static ElevatedCommand? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not (Hold or Release or Enable or Disable or Uninstall or Protect)) return null;

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

        ProtectionChoice? protection = null;
        if (args[0] is Protect)
        {
            bool? autoPause = ValueAfter(args, "--auto-pause") switch { "on" => true, "off" => false, _ => null };
            if (autoPause is null
                || !int.TryParse(ValueAfter(args, "--wait-days"), out var waitDays)
                || !ProtectionSettings.WaitChoices.Contains(waitDays))
                return null;
            protection = new ProtectionChoice(autoPause.Value, waitDays, days);
        }
        return new ElevatedCommand(args[0], kb, days, resultPath, protection);
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
                case Protect: warning = await actions.SetProtectionAsync(Protection!, ct); break;
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
