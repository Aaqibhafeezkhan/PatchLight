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
    string Url,
    string Description = "",
    bool LikelyBusinessOnly = false);

public sealed record PendingUpdate(
    string UpdateId,
    string? Kb,
    string Title,
    Version? Build,
    DateOnly? Released,
    bool IsHidden,
    string? TargetVersion = null)
{
    public bool IsOsUpdate => Build is not null;

    // An optional upgrade to a new Windows version ("Windows 11, version 26H2").
    public bool IsFeatureUpdate => TargetVersion is not null;

    // What people type to pause it: the KB number, or the version for an upgrade
    // (Windows gives upgrades the same KB as that month's security update).
    public string? PauseKey => TargetVersion ?? Kb;
}

public sealed record InstalledUpdate(string? Kb, string Title, Version? Build, DateTimeOffset InstalledOn);

public sealed record UpdateVerdict(
    PendingUpdate Update,
    Light Light,
    string Reason,
    IReadOnlyList<KnownIssue> Issues,
    IReadOnlyList<KnownIssue> Fixes,
    IReadOnlyList<KnownIssue>? SetAside = null,
    IReadOnlyList<NewsArticle>? News = null)
{
    // Open problems that usually only hit business PCs, listed but not counted on this PC.
    public IReadOnlyList<KnownIssue> SetAside { get; init; } = SetAside ?? [];

    // Tech-news reports of problems with this update that Microsoft hasn't confirmed (yet).
    public IReadOnlyList<NewsArticle> News { get; init; } = News ?? [];
}

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

    // A pause planned for an update Windows doesn't offer right now (PatchLight just removed it).
    // It is applied the moment Windows offers that update again.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWaiting => UpdateId.Length == 0;

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
