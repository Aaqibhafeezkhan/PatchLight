using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.Windows;

// Message is what went wrong when it failed, or a warning to show when it worked.
public sealed record ActionResult(bool Succeeded, string? Message);

// The app's state and actions. The window and the tray icon only display it.
public sealed class AppController(Func<Backend> createBackend, AppStateStore stateStore)
{
    private Backend? backend;
    private bool countBusinessByDefault;
    private bool checkAgain;

    public event Action? Changed;
    public event Action<Alert>? AlertRaised;

    public bool IsChecking { get; private set; }
    public CheckResult? Result { get; private set; }
    public IReadOnlyList<UpdateCard> Cards { get; private set; } = [];
    public string? Error { get; private set; }
    public AppState State { get; private set; } = stateStore.Load();
    public bool IsDemo => backend?.IsDemo ?? false;

    // Null while checking or when the check failed: the light shows gray.
    public Light? Light => IsChecking || Error is not null || Result is null ? null : StatusSummary.Overall(Result);

    public string Headline => IsChecking && Result is null ? "Checking..."
        : Error is not null ? "Couldn't check"
        : Result is null ? "Checking..."
        : StatusSummary.Headline(StatusSummary.Overall(Result));

    public string Detail => Error ?? (Result is null
        ? "Looking at Windows Update and Microsoft's known issues."
        : StatusSummary.Detail(Result));

    public string CheckedText => Result is null || Error is not null ? "" : StatusSummary.CheckedText(Result);

    public async Task CheckAsync()
    {
        // A check that is already running may have read Windows Update before a change; run once more after it.
        if (IsChecking)
        {
            checkAgain = true;
            return;
        }
        IsChecking = true;
        Changed?.Invoke();
        do
        {
            checkAgain = false;
            await CheckOnceAsync();
        }
        while (checkAgain);
        IsChecking = false;
        Changed?.Invoke();
    }

    private async Task CheckOnceAsync()
    {
        try
        {
            var result = await Backend().Service.CheckAsync(applyAutoRelease: false, CancellationToken.None);
            Result = result;
            Cards = UpdateCard.Build(result);
            Error = null;

            var (alerts, next) = AlertPlanner.Plan(State, result);
            var all = alerts.ToList();
            if (Backend().BackgroundLogPath is { } logPath && BackgroundCheck.ReadLog(logPath) is { } log)
            {
                // Logs from older versions have no events; their own run is the only one known.
                var events = log.Events.Count > 0 || (log.Resumed.Count == 0 && log.Paused.Count == 0)
                    ? log.Events
                    : [new BackgroundEvent(log.RanAt, log.Resumed, log.Paused)];
                var (backgroundAlerts, afterBackground) = AlertPlanner.PlanBackground(next, log.RanAt, events);
                next = afterBackground;
                all.AddRange(backgroundAlerts);
            }
            SaveState(next);
            foreach (var alert in all) AlertRaised?.Invoke(alert);
        }
        catch (Exception ex)
        {
            Error = Plain(ex);
        }
    }

    public Task<ActionResult> PauseAsync(UpdateCard card) =>
        RunActionAsync(b => b.Actions.HoldAsync(card.PauseKey!, State.PauseDays, CancellationToken.None));

    // Removing an update keeps it paused for the usual pause length once Windows offers it again.
    public Task<ActionResult> UninstallAsync(string kb) =>
        RunActionAsync(b => b.Actions.UninstallAsync(kb, State.PauseDays, CancellationToken.None));

    // Whether problems that usually only hit business PCs color the light. Until the person
    // chooses, the PC decides (work PCs count them).
    public bool CountBusinessIssues => State.CountBusinessIssues ?? (TryBackend() is not null && countBusinessByDefault);

    public async Task SetCheckNewsAsync(bool on)
    {
        SaveState(State with { CheckNews = on });
        if (TryBackend() is { } b) b.Service.News = on ? b.NewsSource : null;
        await CheckAsync();
    }

    public async Task SetCountBusinessIssuesAsync(bool count)
    {
        SaveState(State with { CountBusinessIssues = count });
        if (TryBackend() is { } b) b.Service.CountBusinessIssues = count;
        await CheckAsync();
    }

    public Task<ActionResult> ResumeAsync(UpdateCard card) =>
        RunActionAsync(async b =>
        {
            await b.Actions.ReleaseAsync(card.PauseKey!, CancellationToken.None);
            return null;
        });

    public async Task<(IReadOnlyList<HistoryItem> Items, string? Error)> HistoryAsync()
    {
        try
        {
            return (await Backend().Service.HistoryAsync(60, CancellationToken.None), null);
        }
        catch (Exception ex)
        {
            return ([], Plain(ex));
        }
    }

    public bool StartWithWindows
    {
        get => TryBackend()?.GetStartWithWindows() ?? false;
        set => TryBackend()?.SetStartWithWindows(value);
    }

    // If Task Scheduler can't be asked, the honest answer is that the daily check isn't known to be on.
    public async Task<DailyCheckState> GetDailyCheckAsync()
    {
        try
        {
            return TryBackend() is { } b ? await b.GetDailyCheckAsync() : DailyCheckState.Off;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            return DailyCheckState.Off;
        }
    }

    public Task<ActionResult> SetDailyCheckAsync(bool on) => RunActionAsync(async b =>
    {
        await (on ? b.Actions.EnableDailyCheckAsync(CancellationToken.None) : b.Actions.DisableDailyCheckAsync(CancellationToken.None));
        return null;
    }, recheck: false);

    public string DailyCheckLastRunText()
    {
        if (TryBackend()?.BackgroundLogPath is not { } path || BackgroundCheck.ReadLog(path) is not { } log) return "";
        var when = $"Last ran {log.RanAt.ToLocalTime():MMM d, h:mm tt}";
        if (log.Error is not null) return $"{when}: {log.Error}";
        return log.Resumed.Count == 0 ? $"{when}." : $"{when} and resumed {string.Join(", ", log.Resumed)}.";
    }

    public ProtectionSettings Protection => TryBackend()?.GetProtection() ?? new();
    public bool IsHomeEdition => TryBackend()?.IsHomeEdition ?? false;
    public bool DeferManagedElsewhere => TryBackend()?.DeferManagedElsewhere() ?? false;

    // Automatic pauses last as long as the person's own pauses.
    public Task<ActionResult> SetProtectionAsync(bool autoPauseRed, int waitDays) =>
        RunActionAsync(b => b.Actions.SetProtectionAsync(new ProtectionChoice(autoPauseRed, waitDays, State.PauseDays), CancellationToken.None),
            recheck: false);

    public void SetPauseDays(int days) => SaveState(State with { PauseDays = Math.Clamp(days, 1, HoldPolicy.MaxDays) });

    // Always checks again afterwards, even on failure: the action may have got halfway.
    private async Task<ActionResult> RunActionAsync(Func<Backend, Task<string?>> action, bool recheck = true)
    {
        ActionResult result;
        try
        {
            result = new ActionResult(true, await action(Backend()));
        }
        catch (Exception ex)
        {
            result = new ActionResult(false, Plain(ex));
        }
        if (recheck) await CheckAsync();
        return result;
    }

    private void SaveState(AppState next)
    {
        State = next;
        try
        {
            stateStore.Save(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Worst case an alert shows again next time.
        }
    }

    private Backend Backend()
    {
        if (backend is not null) return backend;
        var created = createBackend();
        countBusinessByDefault = created.Service.CountBusinessIssues;
        if (State.CountBusinessIssues is { } chosen) created.Service.CountBusinessIssues = chosen;
        created.Service.News = State.CheckNews ? created.NewsSource : null;
        return backend = created;
    }

    private Backend? TryBackend()
    {
        try
        {
            return Backend();
        }
        catch (PatchLightException)
        {
            return null;
        }
    }

    private static string Plain(Exception ex)
    {
        if (ex is PatchLightException) return ex.Message;
        ErrorLog.Write(ex);
        return $"Something went wrong. Details were saved to {ErrorLog.Path}.";
    }
}
