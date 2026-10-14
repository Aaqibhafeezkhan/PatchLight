using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using PatchLight.Windows;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class AppControllerTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-controller-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 17, 0, 0, TimeSpan.Zero));
    private readonly GatedUpdateClient updates = new();
    private readonly FakeIssueSource issues;
    private readonly FakeActions actions;
    private readonly AppController controller;
    private readonly PatchLightService service;

    public AppControllerTests()
    {
        ErrorLog.Path = Path.Combine(dir, "last-error.txt");
        issues = new FakeIssueSource(time);
        issues.Issues.Add(Issue(IssueStatus.Confirmed, kb: "KB5127648"));
        updates.Pending.Add(Update());
        actions = new FakeActions(updates);
        service = new PatchLightService(updates, issues, new HoldStore(Path.Combine(dir, "holds.json")), time, "25H2");
        controller = new AppController(() => new Backend
        {
            Service = service,
            NewsSource = new NoNews(),
            Actions = actions,
            GetDailyCheckAsync = () => Task.FromResult(DailyCheckState.Off),
            GetStartWithWindows = () => false,
            SetStartWithWindows = _ => { },
        }, new AppStateStore(Path.Combine(dir, "app-state.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Pausing_while_a_check_runs_shows_the_pause_afterwards()
    {
        await controller.CheckAsync();
        var card = Assert.Single(controller.Cards);

        var gate = new TaskCompletionSource();
        updates.Gate = gate;
        var runningCheck = controller.CheckAsync();     // reads Windows Update before the pause
        var pause = controller.PauseAsync(card);
        gate.SetResult();
        await Task.WhenAll(runningCheck, pause);

        Assert.True((await pause).Succeeded);
        Assert.NotNull(Assert.Single(controller.Cards).PausedText);
    }

    [Fact]
    public async Task Failed_action_still_refreshes_the_list()
    {
        await controller.CheckAsync();
        actions.FailAfterHiding = true;

        var result = await controller.PauseAsync(Assert.Single(controller.Cards));

        Assert.False(result.Succeeded);
        Assert.Equal("Couldn't save.", result.Message);
        Assert.NotNull(Assert.Single(controller.Cards).PausedText);
    }

    [Fact]
    public async Task Warning_from_a_pause_is_passed_on()
    {
        await controller.CheckAsync();
        actions.Warning = "Daily check is off.";

        var result = await controller.PauseAsync(Assert.Single(controller.Cards));

        Assert.True(result.Succeeded);
        Assert.Equal("Daily check is off.", result.Message);
    }

    [Fact]
    public async Task Task_scheduler_trouble_reads_as_daily_check_off()
    {
        var broken = new AppController(() => new Backend
        {
            Service = new PatchLightService(updates, issues, new HoldStore(Path.Combine(dir, "h.json")), time, "25H2"),
            Actions = actions,
            GetDailyCheckAsync = () => throw new System.ComponentModel.Win32Exception(2),
            GetStartWithWindows = () => false,
            SetStartWithWindows = _ => { },
        }, new AppStateStore(Path.Combine(dir, "s.json")));

        Assert.Equal(DailyCheckState.Off, await broken.GetDailyCheckAsync());
    }

    private sealed class NoNews : INewsSource
    {
        public Task<IReadOnlyList<NewsArticle>> GetAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<NewsArticle>>([]);
    }

    private sealed class GatedUpdateClient : IUpdateClient
    {
        private readonly FakeUpdateClient inner = new();
        public List<PendingUpdate> Pending => inner.Pending;
        public TaskCompletionSource? Gate { get; set; }

        public async Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct)
        {
            var snapshot = await inner.GetPendingAsync(ct);
            if (Gate is { } gate)
            {
                Gate = null;
                await gate.Task;
            }
            return snapshot;
        }

        public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct) => inner.SetHiddenAsync(updateId, hidden, ct);
        public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) => inner.GetHistoryAsync(max, ct);
    }

    private sealed class FakeActions(GatedUpdateClient updates) : IElevatedActions
    {
        public bool FailAfterHiding { get; set; }
        public string? Warning { get; set; }

        public async Task<string?> HoldAsync(string kb, int days, CancellationToken ct)
        {
            await updates.SetHiddenAsync("id-" + kb, true, ct);
            if (FailAfterHiding) throw new PatchLightException("Couldn't save.");
            return Warning;
        }

        public Task ReleaseAsync(string kb, CancellationToken ct) => updates.SetHiddenAsync("id-" + kb, false, ct);
        public (string Kb, int Days)? LastUninstall { get; private set; }

        public Task<string?> UninstallAsync(string kb, int days, CancellationToken ct)
        {
            LastUninstall = (kb, days);
            return Task.FromResult<string?>(null);
        }
        public ProtectionChoice? LastProtection { get; private set; }

        public Task<string?> SetProtectionAsync(ProtectionChoice choice, CancellationToken ct)
        {
            LastProtection = choice;
            return Task.FromResult<string?>(null);
        }

        public Task EnableDailyCheckAsync(CancellationToken ct) => Task.CompletedTask;
        public Task DisableDailyCheckAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Business_setting_changes_what_counts()
    {
        issues.Issues.Clear();
        issues.Issues.Add(Issue(IssueStatus.Confirmed, kb: "KB5127648") with { LikelyBusinessOnly = true });

        await controller.SetCountBusinessIssuesAsync(false);
        Assert.Equal(Light.Green, controller.Light);
        Assert.False(controller.CountBusinessIssues);

        await controller.SetCountBusinessIssuesAsync(true);
        Assert.Equal(Light.Red, controller.Light);
        Assert.True(controller.CountBusinessIssues);
    }

    [Fact]
    public async Task Uninstall_passes_the_pause_length()
    {
        await controller.CheckAsync();
        var result = await controller.UninstallAsync("KB5124008");
        Assert.True(result.Succeeded);
        Assert.Equal(("KB5124008", 14), actions.LastUninstall);
    }

    [Fact]
    public async Task Protection_uses_the_pause_length()
    {
        controller.SetPauseDays(21);
        var result = await controller.SetProtectionAsync(true, 7);
        Assert.True(result.Succeeded);
        Assert.Equal(new ProtectionChoice(true, 7, 21), actions.LastProtection);
    }

    [Fact]
    public async Task Turning_news_off_stops_using_it()
    {
        await controller.CheckAsync();
        await controller.SetCheckNewsAsync(false);
        Assert.False(controller.State.CheckNews);
        Assert.Null(service.News);

        await controller.SetCheckNewsAsync(true);
        Assert.NotNull(service.News);
    }
}
