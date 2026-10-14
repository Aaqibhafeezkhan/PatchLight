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
    bool CanResume)
{
    public static IReadOnlyList<UpdateCard> Build(CheckResult result)
    {
        // An update that is ready to resume is still hidden until someone resumes it.
        var holds = result.ActiveHolds.Concat(result.Released.Select(r => r.Hold)).ToList();
        return result.Verdicts
            .OrderByDescending(v => v.Light)
            .ThenBy(v => v.Update.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => From(v, holds.FirstOrDefault(h => h.UpdateId == v.Update.UpdateId)))
            .ToList();
    }

    public static string BadgeText(Light light) => light switch
    {
        Light.Green => "Safe",
        Light.Yellow => "Wait",
        _ => "Problems",
    };

    private static UpdateCard From(UpdateVerdict verdict, Hold? hold)
    {
        var update = verdict.Update;
        var pausedText = !update.IsHidden ? null
            : hold is not null ? $"Paused until {hold.ExpiresOn.ToLocalTime():MMM d}"
            : "Hidden in Windows Update";
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
            CanPause: update.Kb is not null && !update.IsHidden && verdict.Light != Light.Green,
            CanResume: update.IsHidden && hold is not null);
    }
}
