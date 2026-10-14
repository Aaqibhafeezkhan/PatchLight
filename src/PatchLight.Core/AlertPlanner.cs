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
                : "Open PatchLight and press Resume. If the automatic check is on, it does this for you."));
        }

        // Remember only what is true now, so a problem that goes away and comes back alerts again.
        var next = state with
        {
            NotifiedProblems = problems.Select(v => v.Update.UpdateId).ToList(),
            NotifiedFixed = fixedKbs,
        };
        return (alerts, next);
    }

    // The automatic check runs without a window every hour, so the app tells the person what it did since they
    // last heard, the next time the app checks. ranAt: the latest run; events: what recent runs did.
    public static (IReadOnlyList<Alert> Alerts, AppState Next) PlanBackground(AppState state, DateTimeOffset ranAt,
        IReadOnlyList<BackgroundEvent> events)
    {
        if (state.SeenBackgroundRun is { } seen && ranAt <= seen) return ([], state);

        var unseen = events.Where(e => state.SeenBackgroundRun is not { } last || e.At > last).ToList();
        var paused = unseen.SelectMany(e => e.Paused).Distinct().ToList();
        var resumed = unseen.SelectMany(e => e.Resumed).Distinct().ToList();

        var alerts = new List<Alert>();
        if (paused.Count == 1)
            alerts.Add(new Alert($"PatchLight paused {paused[0]} automatically",
                "It has known problems or is too new. PatchLight resumes it once it's safe."));
        else if (paused.Count > 1)
            alerts.Add(new Alert($"PatchLight paused {paused.Count} updates automatically",
                "They have known problems or are too new. PatchLight resumes them once they're safe."));

        if (resumed.Count == 1)
            alerts.Add(new Alert($"PatchLight resumed {resumed[0]}", "Microsoft fixed it or the pause ended. Windows Update will install it."));
        else if (resumed.Count > 1)
            alerts.Add(new Alert($"PatchLight resumed {resumed.Count} updates",
                "Microsoft fixed them or their pauses ended. Windows Update will install them."));

        return (alerts, state with { SeenBackgroundRun = ranAt });
    }
}

// What one run of the automatic check did, kept for a week so the app hears about it even if it wasn't running.
public sealed record BackgroundEvent(DateTimeOffset At, IReadOnlyList<string> Resumed, IReadOnlyList<string> Paused)
{
    public bool Equals(BackgroundEvent? other) =>
        other is not null && At == other.At && Resumed.SequenceEqual(other.Resumed) && Paused.SequenceEqual(other.Paused);

    public override int GetHashCode() => At.GetHashCode();
}
