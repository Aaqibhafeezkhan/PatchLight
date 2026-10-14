namespace PatchLight.Core;

public static class VerdictEngine
{
    public const int NewUpdateWaitDays = 3;

    public static UpdateVerdict Judge(PendingUpdate update, IReadOnlyList<KnownIssue> issues, DateOnly today,
        bool countBusinessIssues = true)
    {
        var open = issues
            .Where(i => i.Status != IssueStatus.Resolved && Affects(i, update.Kb, update.Build))
            .ToList();
        var setAside = countBusinessIssues ? [] : open.Where(i => i.LikelyBusinessOnly).ToList();
        var affecting = open.Except(setAside).ToList();
        var fixes = update.Kb is null
            ? []
            : issues.Where(i => i.ResolvedByKb == update.Kb).ToList();

        var serious = affecting.Where(i => i.Status is IssueStatus.Confirmed or IssueStatus.Investigating).ToList();
        if (serious.Count > 0)
            return new(update, Light.Red, Describe("Known problem", serious, affecting.Count), affecting, fixes, setAside);

        if (affecting.Count > 0)
            return new(update, Light.Yellow, Describe("Known problem with a workaround", affecting, affecting.Count),
                affecting, fixes, setAside);

        if (update.IsOsUpdate && update.Released is { } released)
        {
            var age = today.DayNumber - released.DayNumber;
            if (age < NewUpdateWaitDays)
            {
                var when = age <= 0 ? "Released today." : age == 1 ? "Released 1 day ago." : $"Released {age} days ago.";
                return new(update, Light.Yellow, $"{when} Wait a few days in case problems show up.", affecting, fixes, setAside);
            }
        }

        var safe = setAside.Count switch
        {
            0 => "No known problems.",
            1 => "No known problems for home PCs. 1 problem usually only affects business PCs.",
            _ => $"No known problems for home PCs. {setAside.Count} problems usually only affect business PCs.",
        };
        return new(update, Light.Green, safe, affecting, fixes, setAside);
    }

    // News reports are unconfirmed, so they can make a safe update "wait" but never "problems".
    public static UpdateVerdict WithNews(UpdateVerdict verdict, IReadOnlyList<NewsArticle> reports)
    {
        if (reports.Count == 0) return verdict;
        if (verdict.Light != Light.Green) return verdict with { News = reports };
        var more = reports.Count > 1 ? $" (+{reports.Count - 1} more)" : "";
        return verdict with
        {
            Light = Light.Yellow,
            Reason = $"Problems reported in the news, not confirmed by Microsoft yet: {reports[0].Title}{more}",
            News = reports,
        };
    }

    // An upgrade brings the newest build of that version, so every open problem on its page applies.
    public static UpdateVerdict JudgeUpgrade(PendingUpdate update, IReadOnlyList<KnownIssue> issues, bool countBusinessIssues)
    {
        var name = $"Windows 11, version {update.TargetVersion}";
        var open = issues.Where(i => i.Status != IssueStatus.Resolved).ToList();
        var setAside = countBusinessIssues ? [] : open.Where(i => i.LikelyBusinessOnly).ToList();
        var affecting = open.Except(setAside).ToList();

        var serious = affecting.Where(i => i.Status is IssueStatus.Confirmed or IssueStatus.Investigating).ToList();
        if (serious.Count > 0)
            return new(update, Light.Red, Describe($"{name} has a known problem", serious, affecting.Count), affecting, [], setAside);
        if (affecting.Count > 0)
            return new(update, Light.Yellow, Describe($"{name} has a known problem with a workaround", affecting, affecting.Count),
                affecting, [], setAside);
        return new(update, Light.Green, $"No known problems in {name}.", affecting, [], setAside);
    }

    public static UpdateVerdict UpgradeUnknown(PendingUpdate update) =>
        new(update, Light.Yellow,
            $"Couldn't read Microsoft's known problems for Windows 11, version {update.TargetVersion}. Wait before upgrading.", [], []);

    // Cumulative OS updates carry every change from earlier builds on the same line,
    // so an open issue from build 26200.9550 also affects 26200.9711.
    public static bool Affects(KnownIssue issue, string? kb, Version? build)
    {
        if (kb is not null && issue.OriginatingKb == kb) return true;
        return build is not null
            && issue.OriginatingBuild is not null
            && build.Major == issue.OriginatingBuild.Major
            && build >= issue.OriginatingBuild;
    }

    private static string Describe(string label, List<KnownIssue> lead, int total) =>
        total > 1 ? $"{label}: {lead[0].Title} (+{total - 1} more)" : $"{label}: {lead[0].Title}";
}
