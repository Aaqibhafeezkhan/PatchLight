using PatchLight.Core;

namespace PatchLight.Tests;

public class VerdictEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    public static KnownIssue Issue(IssueStatus status, string? kb = "KB5124010", string? build = "26200.9550",
        string? fixedBy = null, string title = "Remote Desktop might fail") =>
        new("1", title, "", kb, build is null ? null : Version.Parse(build), new DateOnly(2026, 9, 22),
            status, fixedBy, "https://example.test/#1msgdesc");

    public static PendingUpdate Update(string kb = "KB5127648", string? build = "26200.9711",
        DateOnly? released = null, bool hidden = false) =>
        new("id-" + kb, kb, $"2026-10 Security Update ({kb})", build is null ? null : Version.Parse(build),
            released ?? new DateOnly(2026, 10, 1), hidden);

    [Fact]
    public void Open_issue_from_the_same_update_is_red()
    {
        var v = VerdictEngine.Judge(Update("KB5124010", "26200.9550"), [Issue(IssueStatus.Confirmed)], Today);
        Assert.Equal(Light.Red, v.Light);
        Assert.Equal("Known problem: Remote Desktop might fail", v.Reason);
        Assert.Single(v.Issues);
    }

    [Fact]
    public void Later_cumulative_update_inherits_an_open_issue()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Investigating)], Today);
        Assert.Equal(Light.Red, v.Light);
    }

    [Fact]
    public void Older_build_is_not_affected()
    {
        var v = VerdictEngine.Judge(Update("KB5124008", "26200.9445"), [Issue(IssueStatus.Confirmed)], Today);
        Assert.Equal(Light.Green, v.Light);
        Assert.Equal("No known problems.", v.Reason);
    }

    [Fact]
    public void Different_build_line_is_not_affected()
    {
        var v = VerdictEngine.Judge(Update(build: "26300.100"), [Issue(IssueStatus.Confirmed, kb: null)], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Theory]
    [InlineData(IssueStatus.Mitigated)]
    [InlineData(IssueStatus.Unknown)]
    public void Issue_with_workaround_or_unclear_status_is_yellow(IssueStatus status)
    {
        var v = VerdictEngine.Judge(Update(), [Issue(status)], Today);
        Assert.Equal(Light.Yellow, v.Light);
        Assert.Equal("Known problem with a workaround: Remote Desktop might fail", v.Reason);
    }

    [Fact]
    public void Resolved_issue_is_ignored()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Resolved)], Today);
        Assert.Equal(Light.Green, v.Light);
        Assert.Empty(v.Issues);
    }

    [Fact]
    public void Red_wins_over_yellow_and_counts_the_rest()
    {
        var v = VerdictEngine.Judge(Update(),
            [Issue(IssueStatus.Mitigated, title: "Audio"), Issue(IssueStatus.Confirmed, title: "Black screen")], Today);
        Assert.Equal(Light.Red, v.Light);
        Assert.Equal("Known problem: Black screen (+1 more)", v.Reason);
        Assert.Equal(2, v.Issues.Count);
    }

    [Theory]
    [InlineData(0, "Released today. Wait a few days in case problems show up.")]
    [InlineData(1, "Released 1 day ago. Wait a few days in case problems show up.")]
    [InlineData(2, "Released 2 days ago. Wait a few days in case problems show up.")]
    public void Brand_new_os_update_is_yellow(int daysOld, string reason)
    {
        var v = VerdictEngine.Judge(Update(released: Today.AddDays(-daysOld)), [], Today);
        Assert.Equal(Light.Yellow, v.Light);
        Assert.Equal(reason, v.Reason);
    }

    [Fact]
    public void Three_day_old_os_update_is_green()
    {
        var v = VerdictEngine.Judge(Update(released: Today.AddDays(-3)), [], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Fact]
    public void Brand_new_non_os_update_is_green()
    {
        var v = VerdictEngine.Judge(Update("KB2267602", build: null, released: Today), [], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Fact]
    public void Lists_issues_the_update_fixes()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Resolved, fixedBy: "KB5127648")], Today);
        Assert.Single(v.Fixes);
    }
}
