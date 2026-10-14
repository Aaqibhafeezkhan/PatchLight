using PatchLight.Core;

namespace PatchLight.Tests;

public class BusinessOnlyTests
{
    private static IReadOnlyList<KnownIssue> Parse() =>
        ReleaseHealthParser.Parse(ReleaseHealthParserTests.FixtureHtml(), ReleaseHealthParserTests.Url);

    [Fact]
    public void Reads_the_description_of_each_issue()
    {
        var issue = Parse().Single(i => i.Id == "3781");
        Assert.StartsWith("Following installation of Windows updates released September 22, 2026", issue.Description);
    }

    [Fact]
    public void Skips_update_notes_when_reading_the_description() =>
        Assert.DoesNotContain("Updated (9/14)", Parse().Single(i => i.Id == "4983").Description);

    [Theory]
    [InlineData("4990")] // "Domain-joined devices" in the title
    [InlineData("4981")] // "Remote Desktop Services" in the title
    public void Business_issues_are_recognized(string id) =>
        Assert.True(Parse().Single(i => i.Id == id).LikelyBusinessOnly);

    [Theory]
    [InlineData("5006")] // black screen "primarily observed" on Azure Virtual Desktop: home PCs can still get it
    [InlineData("5031")] // "primarily affects" enterprise-managed devices
    [InlineData("3781")] // AC-3 audio
    [InlineData("4984")] // USB audio
    [InlineData("4983")] // Hyper-V Linux VMs: home users run these too
    [InlineData("4959")] // Teams and Outlook on ARM
    [InlineData("3734")] // Desktop background
    [InlineData("4956")] // Defender notifications
    [InlineData("3596")] // Mouse settings
    public void Home_issues_are_not_set_aside(string id) =>
        Assert.False(Parse().Single(i => i.Id == id).LikelyBusinessOnly);

    [Fact]
    public void Microsofts_home_users_sentence_counts() =>
        Assert.True(BusinessSignals.IsLikelyBusinessOnly("Printing might fail", "",
            "Home users of Windows are unlikely to experience this issue."));

    [Fact]
    public void Workaround_style_mentions_of_group_policy_dont_count() =>
        Assert.False(BusinessSignals.IsLikelyBusinessOnly("Apps might close", "",
            "IT administrators can apply the Group Policy listed below to roll back the change."));

    [Fact]
    public void Missing_description_still_parses()
    {
        var html = ReleaseHealthParserTests.FixtureHtml().Replace("id=\"3781msgdesc\"", "id=\"gone\"");
        var issue = ReleaseHealthParser.Parse(html, ReleaseHealthParserTests.Url).Single(i => i.Id == "3781");
        Assert.Equal("", issue.Description);
        Assert.False(issue.LikelyBusinessOnly);
    }

    [Fact]
    public void Hedged_wording_keeps_the_problem_counted() =>
        Assert.False(BusinessSignals.IsLikelyBusinessOnly("Apps might close", "",
            "This issue mostly affects domain-joined devices."));

    [Fact]
    public void Sentences_that_rule_something_out_dont_count() =>
        Assert.False(BusinessSignals.IsLikelyBusinessOnly("Notifications might be wrong", "",
            "This affects home PCs. This issue does not affect Windows 365 or Azure Virtual Desktop."));

    [Fact]
    public void Passing_mentions_of_windows_server_dont_count() =>
        Assert.False(BusinessSignals.IsLikelyBusinessOnly("Defender notifications are wrong", "",
            "This can happen on any version of Windows or Windows Server with Microsoft Defender Antivirus."));

    [Fact]
    public void Virtual_desktop_wording_counts_only_in_the_title()
    {
        Assert.False(BusinessSignals.IsLikelyBusinessOnly("Black screen after sign-in", "Seen in virtual desktop environments.", ""));
        Assert.True(BusinessSignals.IsLikelyBusinessOnly("Azure Virtual Desktop sessions might disconnect", "", ""));
    }
}
