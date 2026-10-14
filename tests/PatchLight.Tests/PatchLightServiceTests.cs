using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class PatchLightServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateClient _updates = new();
    private readonly FakeIssueSource _issues;
    private readonly HoldStore _holds;
    private readonly PatchLightService _service;

    public PatchLightServiceTests()
    {
        _issues = new FakeIssueSource(_time);
        _holds = new HoldStore(Path.Combine(_dir, "holds.json"));
        _service = new PatchLightService(_updates, _issues, _holds, _time, "25H2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private async Task<Hold> HoldBadUpdate(int days = 14)
    {
        _updates.Pending.Add(Update());
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));
        return await _service.HoldAsync("5127648", days, "Paused by you", CancellationToken.None);
    }

    [Fact]
    public async Task Check_gives_every_pending_update_a_light()
    {
        _updates.Pending.Add(Update());
        _updates.Pending.Add(Update("KB2267602", build: null));
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(new[] { Light.Red, Light.Green }, result.Verdicts.Select(v => v.Light));
        Assert.Equal(_time.GetUtcNow(), result.CheckedAt);
    }

    [Fact]
    public async Task Hold_hides_the_update_for_14_days()
    {
        var hold = await HoldBadUpdate();

        Assert.Equal(("id-KB5127648", true), Assert.Single(_updates.HideCalls));
        Assert.Equal("KB5127648", hold.Kb);
        Assert.Equal(_time.GetUtcNow().AddDays(14), hold.ExpiresOn);
        Assert.Equal(hold, Assert.Single(_holds.Load()));
    }

    [Fact]
    public async Task Hold_never_lasts_more_than_35_days()
    {
        var hold = await HoldBadUpdate(days: 400);
        Assert.Equal(_time.GetUtcNow().AddDays(HoldPolicy.MaxDays), hold.ExpiresOn);
    }

    [Fact]
    public async Task Hold_rejects_text_that_is_not_a_kb()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.HoldAsync("hello", 14, "Paused by you", CancellationToken.None));
        Assert.Contains("KB5127648", ex.Message);
    }

    [Fact]
    public async Task Hold_of_an_update_that_is_not_waiting_fails_and_stores_nothing()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.HoldAsync("KB5000000", 14, "Paused by you", CancellationToken.None));
        Assert.Contains("KB5000000", ex.Message);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Hold_that_could_not_hide_stores_nothing()
    {
        _updates.HideFailure = new PatchLightException("Run PatchLight as administrator to pause or resume updates.");

        await Assert.ThrowsAsync<PatchLightException>(() => HoldBadUpdate());

        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_keeps_the_hold_while_the_problem_is_open()
    {
        await HoldBadUpdate();

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Single(result.ActiveHolds);
        Assert.Empty(result.Released);
        Assert.Single(_updates.HideCalls);
    }

    [Fact]
    public async Task Check_resumes_the_update_once_microsoft_fixes_it()
    {
        await HoldBadUpdate();
        _issues.Issues[0] = _issues.Issues[0] with { Status = IssueStatus.Resolved };

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Fixed, Assert.Single(result.Released).Reason);
        Assert.True(result.ReleaseApplied);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_resumes_the_update_when_the_pause_ends()
    {
        await HoldBadUpdate(days: 7);
        _time.Advance(TimeSpan.FromDays(7));

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Expired, Assert.Single(result.Released).Reason);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
    }

    [Fact]
    public async Task Check_quietly_drops_a_hold_when_windows_replaced_the_update()
    {
        await HoldBadUpdate();
        _updates.Pending.Clear();

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Gone, Assert.Single(result.Released).Reason);
        Assert.Single(_updates.HideCalls);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_without_admin_reports_but_changes_nothing()
    {
        await HoldBadUpdate();
        _issues.Issues.Clear();

        var result = await _service.CheckAsync(applyAutoRelease: false, CancellationToken.None);

        Assert.False(result.ReleaseApplied);
        Assert.Single(result.Released);
        Assert.Single(_updates.HideCalls);
        Assert.Single(_holds.Load());
    }

    [Fact]
    public async Task Release_unhides_and_forgets_the_hold()
    {
        await HoldBadUpdate();

        var released = await _service.ReleaseAsync("kb5127648", CancellationToken.None);

        Assert.Equal(ReleaseReason.Manual, released.Reason);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Release_of_something_not_paused_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.ReleaseAsync("KB5127648", CancellationToken.None));
        Assert.Contains("isn't paused", ex.Message);
    }

    [Fact]
    public async Task History_points_at_the_update_that_started_an_open_problem()
    {
        var day = _time.GetUtcNow();
        _updates.History.Add(new InstalledUpdate("KB5124008", "2026-09 Security Update (KB5124008) (26200.9445)",
            new Version(26200, 9445), day.AddDays(-30)));
        _updates.History.Add(new InstalledUpdate("KB5124010", "2026-09 Preview Update (KB5124010) (26200.9550)",
            new Version(26200, 9550), day.AddDays(-20)));
        _updates.History.Add(new InstalledUpdate(null, "9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2", null, day.AddDays(-1)));
        _issues.Issues.Add(Issue(IssueStatus.Mitigated));
        _issues.Issues.Add(Issue(IssueStatus.Resolved, kb: "KB5124008", build: "26200.9445", title: "Old fixed bug"));

        var history = await _service.HistoryAsync(days: 60, CancellationToken.None);

        Assert.Equal(new[] { "KB5124010", "KB5124008" }, history.Select(h => h.Update.Kb));
        Assert.Single(history[0].Issues);
        Assert.Empty(history[1].Issues);
    }

    [Fact]
    public async Task History_blames_the_first_later_build_when_the_original_is_not_installed()
    {
        var day = _time.GetUtcNow();
        _updates.History.Add(new InstalledUpdate("KB5127648", "2026-10 Security Update (KB5127648) (26200.9711)",
            new Version(26200, 9711), day.AddDays(-1)));
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var history = await _service.HistoryAsync(days: 60, CancellationToken.None);

        Assert.Single(Assert.Single(history).Issues);
    }

    [Fact]
    public async Task Pausing_an_update_with_no_problem_survives_a_check()
    {
        _updates.Pending.Add(Update());
        var hold = await _service.HoldAsync("KB5127648", 14, "Paused by you", CancellationToken.None);
        Assert.Empty(hold.IssueIds);

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Single(result.ActiveHolds);
        Assert.Empty(result.Released);
        Assert.Single(_updates.HideCalls);
    }

    [Fact]
    public async Task Hold_remembers_which_problems_justified_it()
    {
        var hold = await HoldBadUpdate();
        Assert.Equal(new[] { "1" }, hold.IssueIds);
    }

    [Fact]
    public async Task Hold_that_cannot_be_saved_puts_the_update_back()
    {
        _updates.Pending.Add(Update());
        Directory.CreateDirectory(Path.Combine(_dir, "holds.json"));

        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.HoldAsync("KB5127648", 14, "Paused by you", CancellationToken.None));

        Assert.Contains("Couldn't save the pause", ex.Message);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
        Assert.False(_updates.Pending[0].IsHidden);
    }

    [Fact]
    public async Task Release_all_resumes_every_pause_and_forgets_them()
    {
        await HoldBadUpdate();
        _holds.Save([.. _holds.Load(), new Hold("KB1111111", "id-gone", _time.GetUtcNow(), _time.GetUtcNow().AddDays(3), "Paused by you")]);

        var released = await _service.ReleaseAllAsync(CancellationToken.None);

        Assert.Equal(["KB5127648", "KB1111111"], released.Select(h => h.Kb));
        Assert.Contains(("id-KB5127648", false), _updates.HideCalls);
        Assert.DoesNotContain(_updates.HideCalls, c => c.Id == "id-gone");
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Release_all_with_nothing_paused_does_nothing()
    {
        Assert.Empty(await _service.ReleaseAllAsync(CancellationToken.None));
        Assert.Empty(_updates.HideCalls);
    }
}
