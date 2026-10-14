namespace PatchLight.Core;

public enum IssueStatus { Investigating, Confirmed, Mitigated, Resolved, Unknown }

// Order matters: sorting descending puts Red first.
public enum Light { Green, Yellow, Red }

public sealed record KnownIssue(
    string Id,
    string Title,
    string Summary,
    string? OriginatingKb,
    Version? OriginatingBuild,
    DateOnly? OriginatingDate,
    IssueStatus Status,
    string? ResolvedByKb,
    string Url);

public sealed record PendingUpdate(
    string UpdateId,
    string? Kb,
    string Title,
    Version? Build,
    DateOnly? Released,
    bool IsHidden)
{
    public bool IsOsUpdate => Build is not null;
}

public sealed record InstalledUpdate(string? Kb, string Title, Version? Build, DateTimeOffset InstalledOn);

public sealed record UpdateVerdict(
    PendingUpdate Update,
    Light Light,
    string Reason,
    IReadOnlyList<KnownIssue> Issues,
    IReadOnlyList<KnownIssue> Fixes);

// IssueIds: the open problems that justified the pause. Older holds.json files don't have it.
public sealed record Hold(
    string Kb,
    string UpdateId,
    DateTimeOffset HeldOn,
    DateTimeOffset ExpiresOn,
    string Reason,
    IReadOnlyList<string>? IssueIds = null)
{
    public IReadOnlyList<string> IssueIds { get; init; } = IssueIds ?? [];

    // Lists compare by reference by default; a hold read back from disk should equal the one saved.
    public bool Equals(Hold? other) =>
        other is not null && Kb == other.Kb && UpdateId == other.UpdateId && HeldOn == other.HeldOn
        && ExpiresOn == other.ExpiresOn && Reason == other.Reason && IssueIds.SequenceEqual(other.IssueIds);

    public override int GetHashCode() => HashCode.Combine(Kb, UpdateId, HeldOn, ExpiresOn, Reason);
}

public enum ReleaseReason { Fixed, Expired, Gone, Manual }

public sealed record ReleasedHold(Hold Hold, ReleaseReason Reason);

public sealed record IssueSnapshot(IReadOnlyList<KnownIssue> Issues, DateTimeOffset FetchedAt, bool FromCache);

public sealed record CheckResult(
    IReadOnlyList<UpdateVerdict> Verdicts,
    IReadOnlyList<Hold> ActiveHolds,
    IReadOnlyList<ReleasedHold> Released,
    bool ReleaseApplied,
    IssueSnapshot Data,
    DateTimeOffset CheckedAt);

public sealed record HistoryItem(InstalledUpdate Update, IReadOnlyList<KnownIssue> Issues);
