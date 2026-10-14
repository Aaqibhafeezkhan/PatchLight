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
        Assert.Equal("Open PatchLight and press Resume. If the automatic check is on, it does this for you.", alert.Body);
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

    private static BackgroundEvent Event(int hoursAfter, IReadOnlyList<string>? resumed = null, IReadOnlyList<string>? paused = null) =>
        new(Ran.AddHours(hoursAfter), resumed ?? [], paused ?? []);

    [Fact]
    public void Updates_resumed_by_the_automatic_check_alert_once()
    {
        var (alerts, next) = AlertPlanner.PlanBackground(new AppState(), Ran, [Event(0, resumed: ["KB5127648"])]);
        var alert = Assert.Single(alerts);
        Assert.Equal("PatchLight resumed KB5127648", alert.Title);
        Assert.Equal("Microsoft fixed it or the pause ended. Windows Update will install it.", alert.Body);
        Assert.Equal(Ran, next.SeenBackgroundRun);
        Assert.Empty(AlertPlanner.PlanBackground(next, Ran, [Event(0, resumed: ["KB5127648"])]).Alerts);
    }

    [Fact]
    public void Several_resumed_updates_make_one_alert()
    {
        var (alerts, _) = AlertPlanner.PlanBackground(new AppState(), Ran, [Event(0, resumed: ["KB1000001", "KB1000002"])]);
        Assert.Equal("PatchLight resumed 2 updates", Assert.Single(alerts).Title);
    }

    [Fact]
    public void Run_that_did_nothing_is_quiet_but_remembered()
    {
        var (alerts, next) = AlertPlanner.PlanBackground(new AppState(), Ran, []);
        Assert.Empty(alerts);
        Assert.Equal(Ran, next.SeenBackgroundRun);
    }

    [Fact]
    public void Automatic_pause_alerts()
    {
        var (alerts, _) = AlertPlanner.PlanBackground(new AppState(), Ran, [Event(0, paused: ["KB5127648"])]);
        var alert = Assert.Single(alerts);
        Assert.Equal("PatchLight paused KB5127648 automatically", alert.Title);
        Assert.Equal("It has known problems or is too new. PatchLight resumes it once it's safe.", alert.Body);

        var (several, _) = AlertPlanner.PlanBackground(new AppState(), Ran, [Event(0, paused: ["KB1", "KB2"])]);
        Assert.Equal("PatchLight paused 2 updates automatically", Assert.Single(several).Title);
    }

    [Fact]
    public void Everything_since_the_last_look_is_told_once()
    {
        var seen = new AppState(SeenBackgroundRun: Ran);
        var events = new[] { Event(0, paused: ["KB0"]), Event(1, paused: ["KB5127648"]), Event(2, resumed: ["KB5127001"]) };

        var (alerts, next) = AlertPlanner.PlanBackground(seen, Ran.AddHours(3), events);

        Assert.Equal(["PatchLight paused KB5127648 automatically", "PatchLight resumed KB5127001"], alerts.Select(a => a.Title));
        Assert.Equal(Ran.AddHours(3), next.SeenBackgroundRun);
    }
}
