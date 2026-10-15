using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class ProtectionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-protect-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 17, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateClient updates = new();
    private readonly FakeIssueSource issues;
    private readonly HoldStore holds;
    private readonly PatchLightService service;

    private static readonly ProtectionSettings AutoPause = new(AutoPauseRed: true);

    public ProtectionTests()
    {
        issues = new FakeIssueSource(time);
        holds = new HoldStore(Path.Combine(dir, "holds.json"));
        service = new PatchLightService(updates, issues, holds, time, "25H2");
    }

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private async Task<PatchLightService.ProtectionOutcome> Protect(ProtectionSettings settings, bool waitWithHolds = false)
    {
        var result = await service.CheckAsync(applyAutoRelease: true, CancellationToken.None);
        return await service.ProtectAsync(result, settings, waitWithHolds, CancellationToken.None);
    }

    private Hold PastHold(string reason, IReadOnlyList<string> ids, int endedDaysAgo = 1) =>
        new("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-15), time.GetUtcNow().AddDays(-endedDaysAgo), reason, ids);

    [Fact]
    public async Task Red_update_is_paused_automatically()
    {
        updates.Pending.Add(Update());
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var outcome = await Protect(AutoPause with { PauseDays = 10 });

        var hold = Assert.Single(outcome.Paused);
        Assert.Equal("KB5127648", hold.Kb);
        Assert.Equal(PatchLightService.AutoReason, hold.Reason);
        Assert.Equal(time.GetUtcNow().AddDays(10), hold.ExpiresOn);
        Assert.Equal(["1"], hold.IssueIds);
        Assert.Contains(("id-KB5127648", true), updates.HideCalls);
        Assert.Equal(hold, Assert.Single(holds.Load()));
    }

    [Fact]
    public async Task Nothing_happens_when_protection_is_off()
    {
        updates.Pending.Add(Update(released: new DateOnly(2026, 10, 12)));
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var outcome = await Protect(new ProtectionSettings(AutoPauseRed: false), waitWithHolds: true);

        Assert.Empty(outcome.Paused);
        Assert.Empty(updates.HideCalls);
    }

    [Fact]
    public async Task Yellow_and_green_are_not_paused()
    {
        updates.Pending.Add(Update());
        updates.Pending.Add(Update("KB890830", build: null));
        issues.Issues.Add(Issue(IssueStatus.Mitigated));

        Assert.Empty((await Protect(AutoPause)).Paused);
        Assert.Empty(updates.HideCalls);
    }

    [Fact]
    public async Task Update_already_paused_is_left_alone()
    {
        updates.Pending.Add(Update(hidden: true));
        issues.Issues.Add(Issue(IssueStatus.Confirmed));
        var existing = new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-1), time.GetUtcNow().AddDays(5), "Paused by you", ["1"]);
        holds.Save([existing]);

        Assert.Empty((await Protect(AutoPause)).Paused);
        Assert.Equal(existing, Assert.Single(holds.Load()));
    }

    [Fact]
    public async Task Update_hidden_by_someone_else_is_left_alone()
    {
        updates.Pending.Add(Update(hidden: true));
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        Assert.Empty((await Protect(AutoPause)).Paused);
        Assert.Empty(holds.Load());
    }

    [Fact]
    public async Task Update_let_through_stays_let_through_until_it_gets_a_new_problem()
    {
        updates.Pending.Add(Update());
        issues.Issues.Add(Issue(IssueStatus.Confirmed));
        var letThrough = AutoPause with { LetThrough = [new LetThrough("KB5127648", ["1"])] };

        Assert.Empty((await Protect(letThrough)).Paused);

        issues.Issues.Add(Issue(IssueStatus.Confirmed) with { Id = "2" });
        var hold = Assert.Single((await Protect(letThrough)).Paused);
        Assert.Equal(["1", "2"], hold.IssueIds);
    }

    [Fact]
    public async Task Pause_that_ran_out_is_not_started_again()
    {
        updates.Pending.Add(Update(hidden: true));
        issues.Issues.Add(Issue(IssueStatus.Confirmed));
        holds.Save([PastHold("Paused by you", ["1"])]);

        var outcome = await Protect(AutoPause);

        Assert.Empty(outcome.Paused);
        Assert.Equal([new LetThrough("KB5127648", ["1"])], outcome.LetThrough);
        Assert.False(Assert.Single(updates.Pending).IsHidden);
    }

    [Fact]
    public async Task Let_through_is_forgotten_once_the_update_is_gone()
    {
        updates.Pending.Add(Update("KB890830", build: null));
        var outcome = await Protect(AutoPause with { LetThrough = [new LetThrough("KB5127648", ["1"])] });
        Assert.Empty(outcome.LetThrough);
    }

    [Fact]
    public async Task Young_os_update_waits_on_home_pcs()
    {
        updates.Pending.Add(Update(released: new DateOnly(2026, 10, 11)));

        var hold = Assert.Single((await Protect(new ProtectionSettings(WaitDays: 7), waitWithHolds: true)).Paused);

        Assert.Equal(PatchLightService.WaitReason, hold.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), hold.ExpiresOn);
        Assert.Empty(hold.IssueIds);
        Assert.Contains(("id-KB5127648", true), updates.HideCalls);
    }

    [Fact]
    public async Task Waiting_update_resumed_by_hand_is_not_paused_again()
    {
        updates.Pending.Add(Update(released: new DateOnly(2026, 10, 11)));
        var resumed = new ProtectionSettings(WaitDays: 7, LetThrough: [new LetThrough("KB5127648", [])]);

        Assert.Empty((await Protect(resumed, waitWithHolds: true)).Paused);
        Assert.Empty(updates.HideCalls);
    }

    [Fact]
    public async Task Only_young_os_updates_wait()
    {
        updates.Pending.Add(Update(released: new DateOnly(2026, 10, 1)));
        updates.Pending.Add(Update("KB890830", build: null, released: new DateOnly(2026, 10, 12)));
        updates.Pending.Add(new PendingUpdate("id-26H2", "KB5127648", "Windows 11, version 26H2", null,
            new DateOnly(2026, 10, 12), false, "26H2"));

        Assert.Empty((await Protect(new ProtectionSettings(WaitDays: 7), waitWithHolds: true)).Paused);
        Assert.Empty(updates.HideCalls);
    }

    [Fact]
    public async Task Wait_uses_windows_itself_when_not_asked_to_pause()
    {
        updates.Pending.Add(Update(released: new DateOnly(2026, 10, 11)));
        Assert.Empty((await Protect(new ProtectionSettings(WaitDays: 7), waitWithHolds: false)).Paused);
    }

    [Fact]
    public async Task Red_update_whose_wait_ended_is_paused_right_away()
    {
        updates.Pending.Add(Update(hidden: true));
        issues.Issues.Add(Issue(IssueStatus.Confirmed));
        holds.Save([PastHold(PatchLightService.WaitReason, [])]);

        var outcome = await Protect(AutoPause with { WaitDays = 7 }, waitWithHolds: true);

        var hold = Assert.Single(outcome.Paused);
        Assert.Equal(PatchLightService.AutoReason, hold.Reason);
        Assert.Empty(outcome.LetThrough);
        Assert.True(Assert.Single(updates.Pending).IsHidden);
    }
}
