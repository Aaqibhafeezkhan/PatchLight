using PatchLight.Core;

namespace PatchLight.Tests;

public class ReleaseHealthParserTests
{
    public const string Url = "https://learn.microsoft.com/en-us/windows/release-health/status-windows-11-25h2";

    public static string FixtureHtml() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "status-windows-11-25h2.html"));

    private static IReadOnlyList<KnownIssue> Parse() => ReleaseHealthParser.Parse(FixtureHtml(), Url);

    [Fact]
    public void Reads_every_row_of_the_summary_table() => Assert.Equal(11, Parse().Count);

    [Fact]
    public void Reads_an_open_issue()
    {
        var issue = Parse().Single(i => i.Id == "5031");
        Assert.Equal("Remote Desktop connections might fail for non-admin users with error 0xC000000D", issue.Title);
        Assert.StartsWith("This issue affects non-admin users", issue.Summary);
        Assert.Equal("KB5124010", issue.OriginatingKb);
        Assert.Equal(new Version(26200, 9550), issue.OriginatingBuild);
        Assert.Equal(new DateOnly(2026, 9, 22), issue.OriginatingDate);
        Assert.Equal(IssueStatus.Mitigated, issue.Status);
        Assert.Null(issue.ResolvedByKb);
        Assert.Equal(Url + "#5031msgdesc", issue.Url);
    }

    [Fact]
    public void Reads_which_update_resolved_an_issue()
    {
        var issue = Parse().Single(i => i.Id == "5006");
        Assert.Equal(IssueStatus.Resolved, issue.Status);
        Assert.Equal("KB5127648", issue.ResolvedByKb);
        Assert.Equal("KB5120998", issue.OriginatingKb);
    }

    [Fact]
    public void Handles_issues_without_an_originating_update()
    {
        var issue = Parse().Single(i => i.Id == "4956");
        Assert.Null(issue.OriginatingKb);
        Assert.Null(issue.OriginatingBuild);
        Assert.Null(issue.OriginatingDate);
        Assert.Equal(IssueStatus.Resolved, issue.Status);
    }

    [Fact]
    public void Throws_a_clear_error_when_the_table_is_missing()
    {
        var ex = Assert.Throws<ReleaseHealthFormatException>(() =>
            ReleaseHealthParser.Parse("<html><body><p>Page moved</p></body></html>", Url));
        Assert.Contains("known issues", ex.Message);
    }

    [Theory]
    [InlineData("Resolved KB5127648", IssueStatus.Resolved)]
    [InlineData("Mitigated", IssueStatus.Mitigated)]
    [InlineData("Mitigated External", IssueStatus.Mitigated)]
    [InlineData("Confirmed", IssueStatus.Confirmed)]
    [InlineData("Investigating", IssueStatus.Investigating)]
    [InlineData("Reported", IssueStatus.Investigating)]
    [InlineData("Something new", IssueStatus.Unknown)]
    public void Maps_status_text(string text, IssueStatus expected) =>
        Assert.Equal(expected, ReleaseHealthParser.ParseStatus(text));
}
