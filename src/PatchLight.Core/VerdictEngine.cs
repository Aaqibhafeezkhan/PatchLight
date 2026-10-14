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
