namespace PatchLight.Core;

public sealed record Alert(string Title, string Body);

// Decides which notifications to show after a check, so the same news is never shown twice.
public static class AlertPlanner
{
    public static (IReadOnlyList<Alert> Alerts, AppState Next) Plan(AppState state, CheckResult result)
    {
        var alerts = new List<Alert>();

        var problems = result.Verdicts.Where(v => v.Light == Light.Red && !v.Update.IsHidden).ToList();
        var newProblems = problems.Where(v => !state.NotifiedProblems.Contains(v.Update.UpdateId)).ToList();
        if (newProblems.Count == 1)
        {
            var v = newProblems[0];
            alerts.Add(new Alert($"Don't install {v.Update.Kb ?? v.Update.Title} yet",
                $"{v.Reason.TrimEnd('.')}. Open PatchLight to pause it."));
        }
        else if (newProblems.Count > 1)
        {
            alerts.Add(new Alert($"{newProblems.Count} updates have known problems",
                "Open PatchLight to see them and pause them."));
        }

        var fixedKbs = result.Released.Where(r => r.Reason == ReleaseReason.Fixed).Select(r => r.Hold.Kb).ToList();
        foreach (var kb in fixedKbs.Where(kb => !state.NotifiedFixed.Contains(kb)))
        {
            alerts.Add(new Alert($"Microsoft fixed {kb}", result.ReleaseApplied
                ? "PatchLight resumed it. Windows Update will install it."
                : "The daily check will resume it, or open PatchLight to resume it now."));
        }

        // Remember only what is true now, so a problem that goes away and comes back alerts again.
        var next = state with
        {
            NotifiedProblems = problems.Select(v => v.Update.UpdateId).ToList(),
            NotifiedFixed = fixedKbs,
        };
        return (alerts, next);
    }

    // The daily check runs without a window, so the app tells the person what it did the next time it checks.
    public static (Alert? Alert, AppState Next) PlanBackground(AppState state, DateTimeOffset ranAt, IReadOnlyList<string> resumed)
    {
        if (state.SeenBackgroundRun is { } seen && ranAt <= seen) return (null, state);

        var next = state with { SeenBackgroundRun = ranAt };
        var alert = resumed.Count switch
        {
            0 => null,
            1 => new Alert($"PatchLight resumed {resumed[0]}", "Microsoft fixed it or the pause ended. Windows Update will install it."),
            _ => new Alert($"PatchLight resumed {resumed.Count} updates", "Microsoft fixed them or their pauses ended. Windows Update will install them."),
        };
        return (alert, next);
    }
}
