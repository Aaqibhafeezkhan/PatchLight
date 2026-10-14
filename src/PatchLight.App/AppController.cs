using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.App;

// The app's state and actions. The window and the tray icon only display it.
public sealed class AppController(Func<Backend> createBackend, AppStateStore stateStore)
{
    private Backend? backend;

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
        if (IsChecking) return;
        IsChecking = true;
        Changed?.Invoke();
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
                var (backgroundAlert, afterBackground) = AlertPlanner.PlanBackground(next, log.RanAt, log.Resumed);
                next = afterBackground;
                if (backgroundAlert is not null) all.Add(backgroundAlert);
            }
            SaveState(next);
            foreach (var alert in all) AlertRaised?.Invoke(alert);
        }
        catch (Exception ex)
        {
            Error = Plain(ex);
        }
        finally
        {
            IsChecking = false;
            Changed?.Invoke();
        }
    }

    // Returns a plain message when it didn't work, or null.
    public Task<string?> PauseAsync(UpdateCard card) =>
        RunActionAsync(b => b.Actions.HoldAsync(card.Kb!, State.PauseDays, CancellationToken.None));

    public Task<string?> ResumeAsync(UpdateCard card) =>
        RunActionAsync(b => b.Actions.ReleaseAsync(card.Kb!, CancellationToken.None));

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

    public async Task<DailyCheckState> GetDailyCheckAsync() =>
        TryBackend() is { } b ? await b.GetDailyCheckAsync() : DailyCheckState.Off;

    public Task<string?> SetDailyCheckAsync(bool on) => RunActionAsync(b => on
        ? b.Actions.EnableDailyCheckAsync(CancellationToken.None)
        : b.Actions.DisableDailyCheckAsync(CancellationToken.None), recheck: false);

    public string DailyCheckLastRunText()
    {
        if (TryBackend()?.BackgroundLogPath is not { } path || BackgroundCheck.ReadLog(path) is not { } log) return "";
        var when = $"Last ran {log.RanAt.ToLocalTime():MMM d, h:mm tt}";
        if (log.Error is not null) return $"{when}: {log.Error}";
        return log.Resumed.Count == 0 ? $"{when}." : $"{when} and resumed {string.Join(", ", log.Resumed)}.";
    }

    public void SetPauseDays(int days) => SaveState(State with { PauseDays = Math.Clamp(days, 1, HoldPolicy.MaxDays) });

    private async Task<string?> RunActionAsync(Func<Backend, Task> action, bool recheck = true)
    {
        try
        {
            await action(Backend());
        }
        catch (Exception ex)
        {
            return Plain(ex);
        }
        if (recheck) await CheckAsync();
        return null;
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

    private Backend Backend() => backend ??= createBackend();

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
