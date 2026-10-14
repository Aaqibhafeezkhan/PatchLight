using PatchLight.Core;
using static PatchLight.Tests.StatusSummaryTests;

namespace PatchLight.Tests;

public class AlertPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static ReleasedHold Fixed(string kb) =>
        new(new Hold(kb, "id-" + kb, Now, Now.AddDays(14), "Paused by you", ["1"]), ReleaseReason.Fixed);

    [Fact]
    public void New_problem_alerts_once()
    {
        var result = Result([Verdict(Light.Red)]);
        var (alerts, next) = AlertPlanner.Plan(new AppState(), result);

        var alert = Assert.Single(alerts);
        Assert.Equal("Don't install KB5127648 yet", alert.Title);
        Assert.Equal("Known problem: Remote Desktop might fail. Open PatchLight to pause it.", alert.Body);
        Assert.Empty(AlertPlanner.Plan(next, result).Alerts);
    }

    [Fact]
    public void Several_new_problems_make_one_alert()
    {
        var (alerts, _) = AlertPlanner.Plan(new AppState(),
            Result([Verdict(Light.Red, "KB1000001"), Verdict(Light.Red, "KB1000002")]));
        var alert = Assert.Single(alerts);
        Assert.Equal("2 updates have known problems", alert.Title);
        Assert.Equal("Open PatchLight to see them and pause them.", alert.Body);
    }

    [Fact]
    public void Problem_that_comes_back_alerts_again()
    {
        var red = Result([Verdict(Light.Red)]);
        var (_, seen) = AlertPlanner.Plan(new AppState(), red);
        var (_, cleared) = AlertPlanner.Plan(seen, Result([Verdict(Light.Green)]));
        Assert.Single(AlertPlanner.Plan(cleared, red).Alerts);
    }

    [Fact]
    public void Paused_or_yellow_updates_never_alert()
    {
        var (alerts, _) = AlertPlanner.Plan(new AppState(),
            Result([Verdict(Light.Red, "KB1000001", hidden: true), Verdict(Light.Yellow, "KB1000002")]));
        Assert.Empty(alerts);
    }

    [Fact]
    public void Fix_waiting_for_the_daily_check_alerts_once()
    {
        var result = Result([Verdict(Light.Green, hidden: true)], released: [Fixed("KB5127648")], applied: false);
        var (alerts, next) = AlertPlanner.Plan(new AppState(), result);

        var alert = Assert.Single(alerts);
        Assert.Equal("Microsoft fixed KB5127648", alert.Title);
        Assert.Equal("Open PatchLight and press Resume. If the daily check is on, it does this for you.", alert.Body);
        Assert.Empty(AlertPlanner.Plan(next, result).Alerts);
    }

    [Fact]
    public void Fix_that_was_applied_says_it_was_resumed()
    {
        var (alerts, _) = AlertPlanner.Plan(new AppState(),
            Result([Verdict(Light.Green)], released: [Fixed("KB5127648")], applied: true));
        Assert.Equal("PatchLight resumed it. Windows Update will install it.", Assert.Single(alerts).Body);
    }

    [Fact]
    public void Expired_pauses_do_not_alert()
    {
        var hold = Fixed("KB5127648").Hold;
        var (alerts, _) = AlertPlanner.Plan(new AppState(),
            Result([Verdict(Light.Green)], released: [new ReleasedHold(hold, ReleaseReason.Expired)], applied: true));
        Assert.Empty(alerts);
    }

    [Fact]
    public void Planning_keeps_the_pause_length()
    {
        var (_, next) = AlertPlanner.Plan(new AppState(PauseDays: 21), Result([Verdict(Light.Red)]));
        Assert.Equal(21, next.PauseDays);
    }
}

public class BackgroundAlertTests
{
    private static readonly DateTimeOffset Ran = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Updates_resumed_by_the_daily_check_alert_once()
    {
        var (alert, next) = AlertPlanner.PlanBackground(new AppState(), Ran, ["KB5127648"]);
        Assert.Equal("PatchLight resumed KB5127648", alert!.Title);
        Assert.Equal("Microsoft fixed it or the pause ended. Windows Update will install it.", alert.Body);
        Assert.Equal(Ran, next.SeenBackgroundRun);
        Assert.Null(AlertPlanner.PlanBackground(next, Ran, ["KB5127648"]).Alert);
    }

    [Fact]
    public void Several_resumed_updates_make_one_alert()
    {
        var (alert, _) = AlertPlanner.PlanBackground(new AppState(), Ran, ["KB1000001", "KB1000002"]);
        Assert.Equal("PatchLight resumed 2 updates", alert!.Title);
    }

    [Fact]
    public void Run_that_resumed_nothing_is_quiet_but_remembered()
    {
        var (alert, next) = AlertPlanner.PlanBackground(new AppState(), Ran, []);
        Assert.Null(alert);
        Assert.Equal(Ran, next.SeenBackgroundRun);
    }
}
