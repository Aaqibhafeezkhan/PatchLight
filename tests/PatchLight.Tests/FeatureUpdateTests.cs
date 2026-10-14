using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

// "Windows 11, version 26H2" style offers: an optional upgrade to a new Windows version, judged against
// Microsoft's known-issues page for that version.
public sealed class FeatureUpdateTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-feature-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public static PendingUpdate Upgrade(string version = "26H2", bool hidden = false) =>
        new("id-upgrade-" + version, null, $"Windows 11, version {version}", null, null, hidden, version);

    [Fact]
    public void Reads_microsofts_26h2_page()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "status-windows-11-26h2.html"));
        var issues = ReleaseHealthParser.Parse(html, ReleaseHealthSource.PageUrl("26H2"));
        Assert.Equal(5, issues.Count);
        Assert.Equal(2, issues.Count(i => i.Status != IssueStatus.Resolved));
    }

    [Theory]
    [InlineData("Windows 11, version 26H2", "26H2")]
    [InlineData("Feature update to Windows 11, version 27H1", "27H1")]
    [InlineData("2026-10 Security Update (KB5127648) (26200.9711)", null)]
    public void Spots_the_target_version(string title, string? version) =>
        Assert.Equal(version, UpdateTitle.TryGetTargetVersion(title));

    [Fact]
    public void Every_open_problem_of_the_new_version_counts()
    {
        var verdict = VerdictEngine.JudgeUpgrade(Upgrade(),
            [Issue(IssueStatus.Mitigated, title: "USB audio"), Issue(IssueStatus.Resolved, title: "Old")], countBusinessIssues: true);

        Assert.Equal(Light.Yellow, verdict.Light);
        Assert.Equal("Windows 11, version 26H2 has a known problem with a workaround: USB audio", verdict.Reason);
        Assert.Single(verdict.Issues);
    }

    [Fact]
    public void Confirmed_problem_makes_the_upgrade_red() =>
        Assert.Equal(Light.Red, VerdictEngine.JudgeUpgrade(Upgrade(), [Issue(IssueStatus.Confirmed)], countBusinessIssues: true).Light);

    [Fact]
    public void Upgrade_with_no_open_problems_is_green()
    {
        var verdict = VerdictEngine.JudgeUpgrade(Upgrade(), [Issue(IssueStatus.Resolved)], countBusinessIssues: true);
        Assert.Equal(Light.Green, verdict.Light);
        Assert.Equal("No known problems in Windows 11, version 26H2.", verdict.Reason);
    }

    [Fact]
    public void Business_problems_of_the_new_version_are_set_aside_on_home_pcs()
    {
        var verdict = VerdictEngine.JudgeUpgrade(Upgrade(),
            [Issue(IssueStatus.Confirmed) with { LikelyBusinessOnly = true }], countBusinessIssues: false);
        Assert.Equal(Light.Green, verdict.Light);
        Assert.Single(verdict.SetAside);
    }

    private (PatchLightService Service, FakeUpdateClient Updates, VersionedIssueSource Issues) Setup()
    {
        var updates = new FakeUpdateClient();
        var issues = new VersionedIssueSource(time);
        var service = new PatchLightService(updates, issues, new HoldStore(Path.Combine(dir, "holds.json")), time, "25H2");
        return (service, updates, issues);
    }

    [Fact]
    public async Task Check_judges_the_upgrade_against_its_own_page()
    {
        var (service, updates, issues) = Setup();
        updates.Pending.Add(Update());
        updates.Pending.Add(Upgrade());
        issues.ByVersion["26H2"] = [Issue(IssueStatus.Confirmed, title: "Only on 26H2") with { Id = "9" }];

        var result = await service.CheckAsync(applyAutoRelease: false, CancellationToken.None);

        Assert.Equal(Light.Green, result.Verdicts.Single(v => v.Update.Kb == "KB5127648").Light);
        var upgrade = result.Verdicts.Single(v => v.Update.IsFeatureUpdate);
        Assert.Equal(Light.Red, upgrade.Light);
        Assert.Equal("Only on 26H2", Assert.Single(upgrade.Issues).Title);
    }

    [Fact]
    public async Task Unreadable_page_for_the_new_version_means_wait_not_failure()
    {
        var (service, updates, issues) = Setup();
        updates.Pending.Add(Upgrade());
        issues.Failing.Add("26H2");

        var verdict = Assert.Single((await service.CheckAsync(false, CancellationToken.None)).Verdicts);

        Assert.Equal(Light.Yellow, verdict.Light);
        Assert.Equal("Couldn't read Microsoft's known problems for Windows 11, version 26H2. Wait before upgrading.", verdict.Reason);
    }

    [Fact]
    public async Task Upgrade_is_paused_by_its_version_not_its_kb()
    {
        var (service, updates, issues) = Setup();
        updates.Pending.Add(Update());                               // KB5127648
        updates.Pending.Add(Upgrade() with { Kb = null });           // Windows reuses KB5127648 for the upgrade
        issues.ByVersion["26H2"] = [Issue(IssueStatus.Confirmed) with { Id = "9" }];

        var hold = await service.HoldAsync("26H2", 14, "Paused by you", CancellationToken.None);

        Assert.Equal("26H2", hold.Kb);
        Assert.Equal("id-upgrade-26H2", hold.UpdateId);
        Assert.Equal(["9"], hold.IssueIds);
        Assert.Equal([("id-upgrade-26H2", true)], updates.HideCalls);
    }

    [Fact]
    public async Task Paused_upgrade_resumes_once_the_new_versions_problems_are_fixed()
    {
        var (service, updates, issues) = Setup();
        updates.Pending.Add(Upgrade());
        issues.ByVersion["26H2"] = [Issue(IssueStatus.Confirmed) with { Id = "9" }];
        await service.HoldAsync("26H2", 14, "Paused by you", CancellationToken.None);

        Assert.Empty((await service.CheckAsync(true, CancellationToken.None)).Released);

        issues.ByVersion["26H2"] = [Issue(IssueStatus.Resolved) with { Id = "9" }];
        Assert.Equal(ReleaseReason.Fixed, Assert.Single((await service.CheckAsync(true, CancellationToken.None)).Released).Reason);
    }

    [Fact]
    public async Task Paused_upgrade_that_windows_stops_offering_keeps_its_pause_until_it_ends()
    {
        var (service, updates, issues) = Setup();
        updates.Pending.Add(Upgrade());
        issues.ByVersion["26H2"] = [Issue(IssueStatus.Confirmed) with { Id = "9" }];
        await service.HoldAsync("26H2", 14, "Paused by you", CancellationToken.None);
        updates.Pending.Clear();   // Microsoft pulled the offer for a while

        var result = await service.CheckAsync(applyAutoRelease: true, CancellationToken.None);
        Assert.Empty(result.Released);
        Assert.Equal("26H2", Assert.Single(result.ActiveHolds).Kb);

        time.Advance(TimeSpan.FromDays(15));
        Assert.Equal(ReleaseReason.Expired, Assert.Single((await service.CheckAsync(true, CancellationToken.None)).Released).Reason);
    }

    [Fact]
    public void Card_offers_pause_by_version() =>
        Assert.Equal("26H2", Assert.Single(UpdateCard.Build(new CheckResult(
            [VerdictEngine.JudgeUpgrade(Upgrade(), [Issue(IssueStatus.Confirmed)], true)], [], [], false,
            new IssueSnapshot([], DateTimeOffset.Now, false), DateTimeOffset.Now))).PauseKey);

    public sealed class VersionedIssueSource(TimeProvider time) : IIssueSource
    {
        public Dictionary<string, List<KnownIssue>> ByVersion { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
            Failing.Contains(windowsVersion)
                ? throw new PatchLightException("Couldn't reach Microsoft.")
                : Task.FromResult(new IssueSnapshot(ByVersion.GetValueOrDefault(windowsVersion, []).ToList(), time.GetUtcNow(), false));
    }
}
