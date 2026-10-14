using PatchLight.Cli;
using PatchLight.Core;
using Spectre.Console.Testing;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public class RenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static CheckResult Result(Light light, bool fromCache = false, IReadOnlyList<ReleasedHold>? released = null,
        bool applied = true)
    {
        var issue = Issue(IssueStatus.Confirmed, title: "Black screen after sign-in");
        var verdict = new UpdateVerdict(Update(), light, "Known problem: Black screen after sign-in",
            light == Light.Green ? [] : [issue], []);
        var data = new IssueSnapshot([issue], fromCache ? Now.AddDays(-2) : Now, fromCache);
        return new CheckResult([verdict], [], released ?? [], applied, data, Now);
    }

    private static string Output(Action<TestConsole> render)
    {
        var console = new TestConsole().Width(200);
        render(console);
        return console.Output;
    }

    [Fact]
    public void Red_update_shows_the_problem_and_how_to_pause_it()
    {
        var text = Output(c => Render.Check(c, Result(Light.Red), isAdmin: false));
        Assert.Contains("Problems", text);
        Assert.Contains("2026-10 Security Update (KB5127648)", text);
        Assert.Contains("Black screen after sign-in", text);
        Assert.Contains("patchlight hold KB5127648", text);
        Assert.Contains("administrator", text);
    }

    [Fact]
    public void Green_update_says_safe() =>
        Assert.Contains("Safe", Output(c => Render.Check(c, Result(Light.Green), isAdmin: true)));

    [Fact]
    public void Says_when_using_the_saved_copy() =>
        Assert.Contains("Couldn't reach Microsoft. Using the copy saved 2 days ago.",
            Output(c => Render.Check(c, Result(Light.Green, fromCache: true), isAdmin: true)));

    [Fact]
    public void Explains_a_resumed_update()
    {
        var hold = new ReleasedHold(new Hold("KB5127648", "id", Now, Now.AddDays(14), "Paused by you"), ReleaseReason.Fixed);
        var text = Output(c => Render.Check(c, Result(Light.Green, released: [hold]), isAdmin: true));
        Assert.Contains("Resumed KB5127648", text);
        Assert.Contains("Microsoft fixed its known problems", text);
    }

    [Fact]
    public void Asks_for_admin_when_a_resume_is_waiting()
    {
        var hold = new ReleasedHold(new Hold("KB5127648", "id", Now, Now.AddDays(14), "Paused by you"), ReleaseReason.Fixed);
        var text = Output(c => Render.Check(c, Result(Light.Green, released: [hold], applied: false), isAdmin: false));
        Assert.Contains("ready to resume", text);
    }

    [Fact]
    public void History_marks_the_likely_cause()
    {
        var item = new HistoryItem(new InstalledUpdate("KB5124010", "2026-09 Preview Update (KB5124010) (26200.9550)",
            new Version(26200, 9550), Now.AddDays(-20)), [Issue(IssueStatus.Confirmed, title: "Remote Desktop fails")]);
        var text = Output(c => Render.History(c, [item]));
        Assert.Contains("Likely cause", text);
        Assert.Contains("Remote Desktop fails", text);
    }

    [Fact]
    public void Pause_message_is_honest_about_needing_admin()
    {
        var hold = new Hold("KB5127648", "id", Now, Now.AddDays(14), "Paused by you");
        var text = Output(c => Render.Held(c, hold));
        Assert.Contains("Paused KB5127648", text);
        Assert.Contains("Run patchlight as administrator", text);
    }

    [Fact]
    public void Says_how_many_business_problems_were_not_counted()
    {
        var issue = Issue(IssueStatus.Confirmed, title: "Domain-joined devices might lose trust") with { LikelyBusinessOnly = true };
        var verdict = new UpdateVerdict(Update(), Light.Green, "No known problems for home PCs.", [], [], [issue]);
        var result = new CheckResult([verdict], [], [], true, new IssueSnapshot([issue], Now, false), Now);

        var text = Output(c => Render.Check(c, result, isAdmin: true));

        Assert.Contains("Domain-joined devices might lose trust", text);
        Assert.Contains("usually only affect business PCs", text);
    }
}
