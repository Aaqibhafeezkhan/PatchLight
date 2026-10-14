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

    private static string PageWithOneRow(string originCell, string statusCell) => $"""
        <html><body><table>
        <thead><tr><th>Summary</th><th>Originating update</th><th>Status</th><th>Last updated</th></tr></thead>
        <tbody><tr>
        <td><div id="9999msg"></div><b>Synthetic issue</b>Synthetic summary</td>
        <td>{originCell}</td><td>{statusCell}</td><td>2026-10-13</td>
        </tr></tbody></table></body></html>
        """;

    [Fact]
    public void Reads_a_resolving_KB_written_as_plain_text_without_a_link()
    {
        var html = PageWithOneRow("OS Build 26200.9550<br><a href=\"x\">KB5124010</a><br>2026-09-22",
            "Resolved KB5127648");
        var issue = ReleaseHealthParser.Parse(html, Url).Single();
        Assert.Equal(IssueStatus.Resolved, issue.Status);
        Assert.Equal("KB5127648", issue.ResolvedByKb);
    }

    [Fact]
    public void Reads_an_originating_KB_written_as_plain_text_before_a_date()
    {
        var html = PageWithOneRow("OS Build 26200.9550<br>KB5124010<br>2026-09-22", "Mitigated");
        var issue = ReleaseHealthParser.Parse(html, Url).Single();
        Assert.Equal("KB5124010", issue.OriginatingKb);
        Assert.Equal(new DateOnly(2026, 9, 22), issue.OriginatingDate);
    }

    [Fact]
    public void Reads_a_7_digit_originating_KB_glued_directly_onto_a_date()
    {
        var html = PageWithOneRow("OS Build 26200.9550<br>KB51240102026-09-22", "Mitigated");
        var issue = ReleaseHealthParser.Parse(html, Url).Single();
        Assert.Equal("KB5124010", issue.OriginatingKb);
        Assert.Equal(new DateOnly(2026, 9, 22), issue.OriginatingDate);
    }

    [Fact]
    public void Odd_dates_and_builds_become_null_instead_of_crashing()
    {
        var html = PageWithOneRow("OS Build 26200.99999999999<br>KB5124010<br>2026-13-45", "Mitigated");
        var issue = ReleaseHealthParser.Parse(html, Url).Single();
        Assert.Null(issue.OriginatingBuild);
        Assert.Null(issue.OriginatingDate);
    }

    [Fact]
    public void A_table_with_rows_but_nothing_readable_is_a_format_error()
    {
        const string html = """
            <html><body><table>
            <thead><tr><th>Summary</th><th>Originating update</th><th>Status</th><th>Last updated</th></tr></thead>
            <tbody><tr><td>no bold title</td><td>a</td><td>b</td><td>c</td></tr></tbody></table></body></html>
            """;
        Assert.Throws<ReleaseHealthFormatException>(() => ReleaseHealthParser.Parse(html, Url));
    }

    [Fact]
    public void A_table_with_no_rows_is_an_empty_list()
    {
        const string html = """
            <html><body><table>
            <thead><tr><th>Summary</th><th>Originating update</th><th>Status</th><th>Last updated</th></tr></thead>
            <tbody></tbody></table></body></html>
            """;
        Assert.Empty(ReleaseHealthParser.Parse(html, Url));
    }
}
