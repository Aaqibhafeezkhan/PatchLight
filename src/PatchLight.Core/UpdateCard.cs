namespace PatchLight.Core;

// One row in the app's update list, with everything the UI needs already decided.
public sealed record UpdateCard(
    string UpdateId,
    string? Kb,
    string Title,
    Light Light,
    string Badge,
    string Reason,
    IReadOnlyList<KnownIssue> Issues,
    int FixCount,
    string? PausedText,
    bool CanPause,
    bool CanResume,
    IReadOnlyList<KnownIssue> SetAside,
    string? PauseKey)
{
    public static IReadOnlyList<UpdateCard> Build(CheckResult result)
    {
        // An update that is ready to resume is still hidden until someone resumes it.
        var holds = result.ActiveHolds.Select(h => (Hold: h, Ended: false))
            .Concat(result.Released.Select(r => (r.Hold, Ended: r.Reason == ReleaseReason.Expired)))
            .ToList();
        // Paused updates can't hurt the PC, so they go last.
        return result.Verdicts
            .OrderBy(v => v.Update.IsHidden)
            .ThenByDescending(v => v.Light)
            .ThenBy(v => v.Update.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => From(v, holds.FirstOrDefault(h => h.Hold.UpdateId == v.Update.UpdateId)))
            .ToList();
    }

    public static string BadgeText(Light light) => light switch
    {
        Light.Green => "Safe",
        Light.Yellow => "Wait",
        _ => "Problems",
    };

    private static UpdateCard From(UpdateVerdict verdict, (Hold Hold, bool Ended) match)
    {
        var update = verdict.Update;
        var hold = match.Hold;
        var pausedText = !update.IsHidden ? null
            : hold is null ? "Hidden in Windows Update"
            : match.Ended ? "Pause ended, press Resume"
            : $"Paused until {hold.ExpiresOn.ToLocalTime():MMM d}";
        return new UpdateCard(
            update.UpdateId,
            update.Kb,
            update.Title,
            verdict.Light,
            BadgeText(verdict.Light),
            verdict.Reason,
            verdict.Issues,
            verdict.Fixes.Count,
            pausedText,
            CanPause: update.PauseKey is not null && !update.IsHidden && verdict.Light != Light.Green,
            CanResume: update.IsHidden && hold is not null,
            SetAside: verdict.SetAside,
            PauseKey: update.PauseKey);
    }
}
