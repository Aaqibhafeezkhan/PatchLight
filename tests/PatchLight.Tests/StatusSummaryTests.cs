using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public class StatusSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static UpdateVerdict Verdict(Light light, string kb = "KB5127648", bool hidden = false,
        IReadOnlyList<KnownIssue>? fixes = null) =>
        new(Update(kb, hidden: hidden), light, light == Light.Green ? "No known problems." : "Known problem: Remote Desktop might fail",
            light == Light.Green ? [] : [Issue(IssueStatus.Confirmed)], fixes ?? []);

    public static CheckResult Result(IReadOnlyList<UpdateVerdict> verdicts, IReadOnlyList<Hold>? active = null,
        IReadOnlyList<ReleasedHold>? released = null, bool applied = false, bool fromCache = false) =>
        new(verdicts, active ?? [], released ?? [], applied,
            new IssueSnapshot([], fromCache ? Now.AddHours(-30) : Now, fromCache), Now);

    [Fact]
    public void Overall_is_the_worst_light_of_updates_that_are_not_paused()
    {
        var result = Result([Verdict(Light.Green, "KB1000001"), Verdict(Light.Red, "KB1000002", hidden: true),
            Verdict(Light.Yellow, "KB1000003")]);
        Assert.Equal(Light.Yellow, StatusSummary.Overall(result));
    }

    [Fact]
    public void Overall_is_green_when_nothing_is_waiting() =>
        Assert.Equal(Light.Green, StatusSummary.Overall(Result([])));

    [Theory]
    [InlineData(Light.Green, "All clear")]
    [InlineData(Light.Yellow, "Wait a few days")]
    [InlineData(Light.Red, "Don't install yet")]
    public void Headline_matches_the_light(Light light, string expected) =>
        Assert.Equal(expected, StatusSummary.Headline(light));

    [Fact]
    public void Detail_when_nothing_is_waiting() =>
        Assert.Equal("Nothing is waiting to install. You're up to date.", StatusSummary.Detail(Result([])));

    [Fact]
    public void Detail_when_everything_is_safe() =>
        Assert.Equal("No known problems with the updates waiting on this PC.",
            StatusSummary.Detail(Result([Verdict(Light.Green)])));

    [Fact]
    public void Detail_counts_yellow_updates()
    {
        Assert.Equal("1 update has a known problem with a workaround or is brand new.",
            StatusSummary.Detail(Result([Verdict(Light.Yellow)])));
        Assert.Equal("2 updates have a known problem with a workaround or are brand new.",
            StatusSummary.Detail(Result([Verdict(Light.Yellow, "KB1000001"), Verdict(Light.Yellow, "KB1000002")])));
    }

    [Fact]
    public void Detail_counts_red_updates_and_ignores_paused_ones()
    {
        Assert.Equal("1 update has known problems. Pause it until Microsoft fixes it.",
            StatusSummary.Detail(Result([Verdict(Light.Red, "KB1000001"), Verdict(Light.Red, "KB1000002", hidden: true)])));
        Assert.Equal("2 updates have known problems. Pause them until Microsoft fixes them.",
            StatusSummary.Detail(Result([Verdict(Light.Red, "KB1000001"), Verdict(Light.Red, "KB1000002")])));
    }

    [Fact]
    public void Detail_when_only_paused_updates_remain() =>
        Assert.Equal("No known problems with the updates waiting on this PC.",
            StatusSummary.Detail(Result([Verdict(Light.Red, hidden: true)])));

    [Fact]
    public void Checked_text_for_fresh_and_saved_data()
    {
        Assert.Equal("Checked against Microsoft's known issues just now.", StatusSummary.CheckedText(Result([])));
        Assert.Equal("Couldn't reach Microsoft. Using the copy saved 30 hours ago.",
            StatusSummary.CheckedText(Result([], fromCache: true)));
    }

    [Theory]
    [InlineData(0.5, "less than an hour ago")]
    [InlineData(5, "5 hours ago")]
    [InlineData(72, "3 days ago")]
    public void Ago_wording(double hours, string expected) =>
        Assert.Equal(expected, StatusSummary.Ago(TimeSpan.FromHours(hours)));

    [Fact]
    public void Waiting_pause_note_while_windows_does_not_offer_the_update()
    {
        var hold = new Hold("KB5124008", "", Now, Now.AddDays(14), "Removed by you");
        var note = Assert.Single(StatusSummary.WaitingNotes(Result([], active: [hold])));
        Assert.StartsWith("You removed KB5124008. When Windows offers it again, PatchLight pauses it", note);
        Assert.Contains("Windows may install it again first", note);
    }

    [Fact]
    public void Waiting_pause_note_when_windows_offers_the_update_again()
    {
        var hold = new Hold("KB5127648", "", Now, Now.AddDays(14), "Removed by you");
        var note = Assert.Single(StatusSummary.WaitingNotes(Result([Verdict(Light.Red)], active: [hold])));
        Assert.Equal("Windows offers KB5127648 again, which you removed. Press Pause now so it isn't installed.", note);
    }
}
