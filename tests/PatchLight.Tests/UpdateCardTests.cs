using PatchLight.Core;
using static PatchLight.Tests.StatusSummaryTests;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public class UpdateCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static Hold HoldFor(string kb) => new(kb, "id-" + kb, Now, Now.AddDays(14), "Paused by you");

    [Fact]
    public void Cards_are_ordered_worst_first_then_by_title()
    {
        var cards = UpdateCard.Build(Result([Verdict(Light.Green, "KB1000003"), Verdict(Light.Red, "KB1000002"),
            Verdict(Light.Yellow, "KB1000005"), Verdict(Light.Yellow, "KB1000004")]));
        Assert.Equal(["KB1000002", "KB1000004", "KB1000005", "KB1000003"], cards.Select(c => c.Kb));
    }

    [Fact]
    public void Red_card_can_be_paused()
    {
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Red)])));
        Assert.Equal("Problems", card.Badge);
        Assert.Equal(Light.Red, card.Light);
        Assert.Equal("Known problem: Remote Desktop might fail", card.Reason);
        Assert.Single(card.Issues);
        Assert.True(card.CanPause);
        Assert.False(card.CanResume);
        Assert.Null(card.PausedText);
    }

    [Theory]
    [InlineData(Light.Green, "Safe", false)]
    [InlineData(Light.Yellow, "Wait", true)]
    public void Only_updates_with_a_reason_to_wait_can_be_paused(Light light, string badge, bool canPause)
    {
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(light)])));
        Assert.Equal(badge, card.Badge);
        Assert.Equal(canPause, card.CanPause);
    }

    [Fact]
    public void Update_without_a_kb_cannot_be_paused()
    {
        var verdict = Verdict(Light.Red);
        verdict = verdict with { Update = verdict.Update with { Kb = null } };
        Assert.False(Assert.Single(UpdateCard.Build(Result([verdict]))).CanPause);
    }

    [Fact]
    public void Paused_update_shows_until_when_and_can_be_resumed()
    {
        var hold = HoldFor("KB5127648");
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Red, hidden: true)], active: [hold])));
        Assert.Equal($"Paused until {hold.ExpiresOn.ToLocalTime():MMM d}", card.PausedText);
        Assert.True(card.CanResume);
        Assert.False(card.CanPause);
    }

    [Fact]
    public void Update_ready_to_resume_can_still_be_resumed()
    {
        var hold = HoldFor("KB5127648");
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Green, hidden: true)],
            released: [new ReleasedHold(hold, ReleaseReason.Fixed)])));
        Assert.True(card.CanResume);
        Assert.NotNull(card.PausedText);
    }

    [Fact]
    public void Update_hidden_outside_patchlight_cannot_be_resumed_here()
    {
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Green, hidden: true)])));
        Assert.Equal("Hidden in Windows Update", card.PausedText);
        Assert.False(card.CanResume);
        Assert.False(card.CanPause);
    }

    [Fact]
    public void Card_lists_problems_set_aside_for_business_pcs()
    {
        var aside = Issue(IssueStatus.Confirmed, title: "Domain-joined devices might lose trust") with { LikelyBusinessOnly = true };
        var verdict = Verdict(Light.Green) with { SetAside = [aside] };
        Assert.Equal("Domain-joined devices might lose trust", Assert.Single(Assert.Single(UpdateCard.Build(Result([verdict]))).SetAside).Title);
    }

    [Fact]
    public void Ended_pause_says_to_press_resume()
    {
        var hold = HoldFor("KB5127648");
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Red, hidden: true)],
            released: [new ReleasedHold(hold, ReleaseReason.Expired)])));
        Assert.Equal("Pause ended, press Resume", card.PausedText);
        Assert.True(card.CanResume);
    }

    [Fact]
    public void Card_carries_the_news_reports()
    {
        var article = new NewsArticle("Example", "KB5127648 breaks Wi-Fi", "https://news.example/0", Now, ["KB5127648"], true);
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Yellow) with { News = [article] }])));
        Assert.Equal("KB5127648 breaks Wi-Fi", Assert.Single(card.News).Title);
    }

    [Fact]
    public void Card_counts_the_problems_an_update_fixes()
    {
        var card = Assert.Single(UpdateCard.Build(Result([Verdict(Light.Green,
            fixes: [Issue(IssueStatus.Resolved), Issue(IssueStatus.Resolved)])])));
        Assert.Equal(2, card.FixCount);
    }
}

public class UpdateCardOrderTests
{
    [Fact]
    public void Paused_updates_go_last()
    {
        var cards = UpdateCard.Build(Result([Verdict(Light.Red, "KB1000001", hidden: true), Verdict(Light.Green, "KB1000002")]));
        Assert.Equal(["KB1000002", "KB1000001"], cards.Select(c => c.Kb));
    }
}
