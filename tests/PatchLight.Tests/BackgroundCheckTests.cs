using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using PatchLight.Windows;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class BackgroundCheckTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-bg-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 17, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateClient updates = new();
    private string LogPath => Path.Combine(dir, "last-background-check.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private PatchLightService Service(IIssueSource issues, HoldStore holds) =>
        new(updates, issues, holds, time, "25H2");

    [Fact]
    public async Task Resumes_a_fixed_update_and_logs_it()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        updates.Pending.Add(Update(hidden: true));
        holds.Save([new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-2), time.GetUtcNow().AddDays(12),
            "Paused by you", ["1"])]);

        var log = await BackgroundCheck.RunAsync(Service(new FakeIssueSource(time), holds), LogPath, time, CancellationToken.None);

        Assert.Equal(["KB5127648"], log.Resumed);
        Assert.Null(log.Error);
        Assert.Equal(time.GetUtcNow(), log.RanAt);
        Assert.Contains(("id-KB5127648", false), updates.HideCalls);
        Assert.Equal(log, BackgroundCheck.ReadLog(LogPath));
    }

    [Fact]
    public async Task Failure_is_logged_not_thrown()
    {
        var log = await BackgroundCheck.RunAsync(
            Service(new ThrowingIssueSource(), new HoldStore(Path.Combine(dir, "holds.json"))), LogPath, time, CancellationToken.None);

        Assert.Equal("No internet.", log.Error);
        Assert.Empty(log.Resumed);
        Assert.Equal("No internet.", BackgroundCheck.ReadLog(LogPath)!.Error);
    }

    [Fact]
    public async Task Expired_pause_ends_even_when_microsoft_cannot_be_reached()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        updates.Pending.Add(Update(hidden: true));
        holds.Save([new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-15), time.GetUtcNow().AddDays(-1),
            "Paused by you", ["1"])]);

        var log = await BackgroundCheck.RunAsync(Service(new ThrowingIssueSource(), holds), LogPath, time, CancellationToken.None);

        Assert.Equal("No internet.", log.Error);
        Assert.Equal(["KB5127648"], log.Resumed);
        Assert.Contains(("id-KB5127648", false), updates.HideCalls);
        Assert.Empty(holds.Load());
    }

    [Fact]
    public async Task Removed_update_is_paused_even_when_microsoft_cannot_be_reached()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        updates.Pending.Add(Update());
        holds.Save([new Hold("KB5127648", "", time.GetUtcNow(), time.GetUtcNow().AddDays(14), "Removed by you")]);

        await BackgroundCheck.RunAsync(Service(new ThrowingIssueSource(), holds), LogPath, time, CancellationToken.None);

        Assert.Contains(("id-KB5127648", true), updates.HideCalls);
        Assert.Equal("id-KB5127648", Assert.Single(holds.Load()).UpdateId);
    }

    [Fact]
    public async Task Pause_that_has_not_ended_stays_when_microsoft_cannot_be_reached()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        updates.Pending.Add(Update(hidden: true));
        holds.Save([new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-1), time.GetUtcNow().AddDays(5),
            "Paused by you", ["1"])]);

        var log = await BackgroundCheck.RunAsync(Service(new ThrowingIssueSource(), holds), LogPath, time, CancellationToken.None);

        Assert.Empty(log.Resumed);
        Assert.Empty(updates.HideCalls);
        Assert.Single(holds.Load());
    }

    [Fact]
    public async Task Red_update_is_paused_and_logged_when_auto_pause_is_on()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        var settings = new ProtectionStore(Path.Combine(dir, "settings.json"));
        settings.Save(new ProtectionSettings(AutoPauseRed: true));
        updates.Pending.Add(Update());
        var issues = new FakeIssueSource(time);
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var log = await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None, settings);

        Assert.Equal(["KB5127648"], log.Paused);
        Assert.Null(log.Error);
        Assert.Equal(["KB5127648"], BackgroundCheck.ReadLog(LogPath)!.Paused);
        Assert.Equal(PatchLightService.AutoReason, Assert.Single(holds.Load()).Reason);
    }

    [Fact]
    public async Task What_each_run_did_is_kept_for_a_week()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        var settings = new ProtectionStore(Path.Combine(dir, "settings.json"));
        settings.Save(new ProtectionSettings(AutoPauseRed: true));
        var issues = new FakeIssueSource(time);
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        updates.Pending.Add(Update());
        await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None, settings);
        time.Advance(TimeSpan.FromHours(1));
        var quiet = await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None, settings);

        Assert.Empty(quiet.Paused);
        var kept = Assert.Single(quiet.Events);
        Assert.Equal(["KB5127648"], kept.Paused);
        Assert.Equal(time.GetUtcNow().AddHours(-1), kept.At);

        time.Advance(TimeSpan.FromDays(8));
        Assert.Empty((await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None, settings)).Events);
    }

    [Fact]
    public async Task Pause_that_ran_out_is_remembered_as_let_through()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        var settings = new ProtectionStore(Path.Combine(dir, "settings.json"));
        settings.Save(new ProtectionSettings(AutoPauseRed: true));
        updates.Pending.Add(Update(hidden: true));
        var issues = new FakeIssueSource(time);
        issues.Issues.Add(Issue(IssueStatus.Confirmed));
        holds.Save([new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-15), time.GetUtcNow().AddDays(-1),
            "Paused by you", ["1"])]);

        var log = await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None, settings);

        Assert.Equal(["KB5127648"], log.Resumed);
        Assert.Empty(log.Paused);
        Assert.Equal([new LetThrough("KB5127648", ["1"])], settings.Load().LetThrough);
    }

    [Fact]
    public async Task Damaged_settings_use_the_defaults()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ nope");
        updates.Pending.Add(Update());
        var issues = new FakeIssueSource(time);
        issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var log = await BackgroundCheck.RunAsync(Service(issues, holds), LogPath, time, CancellationToken.None,
            new ProtectionStore(Path.Combine(dir, "settings.json")), waitWithHolds: true);

        // Auto-pause is on by default; the wait is off.
        Assert.Equal(["KB5127648"], log.Paused);
        Assert.Null(log.Error);
        Assert.Equal(PatchLightService.AutoReason, Assert.Single(holds.Load()).Reason);
    }

    [Fact]
    public void Log_from_an_older_version_reads_with_nothing_paused()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(LogPath, """{ "RanAt": "2026-10-13T17:00:00+00:00", "Resumed": ["KB5127648"], "Error": null }""");
        var log = BackgroundCheck.ReadLog(LogPath)!;
        Assert.Equal(["KB5127648"], log.Resumed);
        Assert.Empty(log.Paused);
    }

    [Fact]
    public void Missing_or_damaged_log_reads_as_nothing()
    {
        Assert.Null(BackgroundCheck.ReadLog(LogPath));
        Directory.CreateDirectory(dir);
        File.WriteAllText(LogPath, "{ nope");
        Assert.Null(BackgroundCheck.ReadLog(LogPath));
    }

    private sealed class ThrowingIssueSource : IIssueSource
    {
        public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
            throw new PatchLightException("No internet.");
    }
}
