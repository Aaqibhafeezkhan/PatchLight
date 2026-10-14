using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class FakeUpdateClient : IUpdateClient
{
    public List<PendingUpdate> Pending { get; } = [];
    public List<InstalledUpdate> History { get; } = [];
    public List<(string Id, bool Hidden)> HideCalls { get; } = [];
    public PatchLightException? HideFailure { get; set; }

    public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PendingUpdate>>(Pending.ToList());

    public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct)
    {
        if (HideFailure is not null) throw HideFailure;
        HideCalls.Add((updateId, hidden));
        var i = Pending.FindIndex(p => p.UpdateId == updateId);
        if (i >= 0) Pending[i] = Pending[i] with { IsHidden = hidden };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InstalledUpdate>>(History.Take(max).ToList());
}

public sealed class FakeIssueSource(TimeProvider time) : IIssueSource
{
    public List<KnownIssue> Issues { get; } = [];

    public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
        Task.FromResult(new IssueSnapshot(Issues.ToList(), time.GetUtcNow(), FromCache: false));
}
