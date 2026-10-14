using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public class SetAsideTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    private static KnownIssue BusinessIssue() =>
        Issue(IssueStatus.Confirmed, title: "Domain-joined devices might lose trust") with { LikelyBusinessOnly = true };

    [Fact]
    public void Business_only_problem_is_set_aside_on_a_home_pc()
    {
        var verdict = VerdictEngine.Judge(Update(), [BusinessIssue()], Today, countBusinessIssues: false);

        Assert.Equal(Light.Green, verdict.Light);
        Assert.Empty(verdict.Issues);
        Assert.Single(verdict.SetAside);
    }

    [Fact]
    public void Safe_reason_mentions_the_problem_set_aside() =>
        Assert.Equal("No known problems for home PCs. 1 problem usually only affects business PCs.",
            VerdictEngine.Judge(Update(), [BusinessIssue()], Today, countBusinessIssues: false).Reason);

    [Fact]
    public void Business_only_problem_counts_when_asked()
    {
        var verdict = VerdictEngine.Judge(Update(), [BusinessIssue()], Today, countBusinessIssues: true);

        Assert.Equal(Light.Red, verdict.Light);
        Assert.Single(verdict.Issues);
        Assert.Empty(verdict.SetAside);
    }

    [Fact]
    public void Home_problem_still_counts_next_to_a_business_one()
    {
        var verdict = VerdictEngine.Judge(Update(), [BusinessIssue(), Issue(IssueStatus.Mitigated, title: "USB audio")],
            Today, countBusinessIssues: false);

        Assert.Equal(Light.Yellow, verdict.Light);
        Assert.Equal("USB audio", Assert.Single(verdict.Issues).Title);
        Assert.Single(verdict.SetAside);
    }

    [Fact]
    public void Resolved_business_problem_is_not_listed_at_all() =>
        Assert.Empty(VerdictEngine.Judge(Update(), [BusinessIssue() with { Status = IssueStatus.Resolved }], Today,
            countBusinessIssues: false).SetAside);

    [Fact]
    public async Task Service_uses_the_setting()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var updates = new FakeUpdateClient();
        updates.Pending.Add(Update());
        var issues = new FakeIssueSource(time);
        issues.Issues.Add(BusinessIssue());
        var service = new PatchLightService(updates, issues,
            new HoldStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")), time, "25H2");

        Assert.Equal(Light.Red, Assert.Single((await service.CheckAsync(false, CancellationToken.None)).Verdicts).Light);
        service.CountBusinessIssues = false;
        Assert.Equal(Light.Green, Assert.Single((await service.CheckAsync(false, CancellationToken.None)).Verdicts).Light);
    }
}
