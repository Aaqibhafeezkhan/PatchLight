using PatchLight.Core;

namespace PatchLight.Windows;

// Outdated: set up by an older PatchLight, so it runs once a day instead of every hour.
public enum DailyCheckState { Off, On, Moved, Outdated }

// Everything the app talks to: the real PC, or the demo.
public sealed class Backend
{
    public required PatchLightService Service { get; init; }
    public required IElevatedActions Actions { get; init; }
    public required Func<Task<DailyCheckState>> GetDailyCheckAsync { get; init; }
    public required Func<bool> GetStartWithWindows { get; init; }
    public required Action<bool> SetStartWithWindows { get; init; }
    public string? BackgroundLogPath { get; init; }
    public bool IsDemo { get; init; }

    // Tech-news reports; the app turns them off when the person does.
    public INewsSource? NewsSource { get; init; }
}
