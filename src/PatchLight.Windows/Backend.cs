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

    // The protection settings as the automatic check sees them.
    public Func<ProtectionSettings> GetProtection { get; init; } = () => new();

    // Home can't use Windows' own deferral, so the wait is done with pauses there.
    public bool IsHomeEdition { get; init; }

    // A company (or something other than PatchLight) already set Windows' deferral policy.
    public Func<bool> DeferManagedElsewhere { get; init; } = () => false;
}
