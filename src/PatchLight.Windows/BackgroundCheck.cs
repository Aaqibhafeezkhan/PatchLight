using System.Text.Json;
using PatchLight.Core;

namespace PatchLight.Windows;

// Resumed/Paused: what this run did (Paused: started by the protection settings). Events: every run of the
// last week that did something, this one included, so the app hears about runs it missed. Older logs have neither.
public sealed record BackgroundCheckLog(DateTimeOffset RanAt, IReadOnlyList<string> Resumed, string? Error,
    IReadOnlyList<string>? Paused = null, IReadOnlyList<BackgroundEvent>? Events = null)
{
    public static readonly TimeSpan KeepEvents = TimeSpan.FromDays(7);

    public IReadOnlyList<string> Paused { get; init; } = Paused ?? [];
    public IReadOnlyList<BackgroundEvent> Events { get; init; } = Events ?? [];

    public bool Equals(BackgroundCheckLog? other) =>
        other is not null && RanAt == other.RanAt && Error == other.Error && Resumed.SequenceEqual(other.Resumed)
        && Paused.SequenceEqual(other.Paused) && Events.SequenceEqual(other.Events);

    public override int GetHashCode() => HashCode.Combine(RanAt, Error);
}

// What the hourly scheduled task does: resume fixed or expired pauses, apply the protection settings,
// then leave a note for the app.
public static class BackgroundCheck
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // waitWithHolds: Windows' own deferral isn't available (Home), so the wait is done with pauses.
    public static async Task<BackgroundCheckLog> RunAsync(
        PatchLightService service, string logPath, TimeProvider time, CancellationToken ct,
        ProtectionStore? protection = null, bool waitWithHolds = false)
    {
        BackgroundCheckLog log;
        try
        {
            var result = await service.CheckAsync(applyAutoRelease: true, ct);
            var resumed = result.Released.Where(r => r.Reason != ReleaseReason.Gone).Select(r => r.Hold.Kb).ToList();
            log = new BackgroundCheckLog(time.GetUtcNow(), resumed, null);
            if (protection is not null)
            {
                try
                {
                    var settings = protection.Load();
                    var outcome = await service.ProtectAsync(result, settings, waitWithHolds, ct);
                    if (!outcome.LetThrough.SequenceEqual(settings.LetThrough))
                        protection.Save(settings with { LetThrough = outcome.LetThrough });
                    log = log with { Paused = outcome.Paused.Select(h => h.Kb).ToList() };
                }
                catch (Exception ex)
                {
                    log = log with { Error = ex is PatchLightException ? ex.Message : $"Something went wrong: {ex.GetType().Name}." };
                }
            }
        }
        catch (Exception ex)
        {
            var message = ex is PatchLightException ? ex.Message : $"Something went wrong: {ex.GetType().Name}.";
            IReadOnlyList<string> ended = [];
            try
            {
                ended = (await service.ReleaseExpiredAsync(ct)).Select(h => h.Kb).ToList();
            }
            catch (Exception)
            {
                // Windows Update itself isn't answering; try again next time.
            }
            log = new BackgroundCheckLog(time.GetUtcNow(), ended, message);
        }

        var cutoff = log.RanAt - BackgroundCheckLog.KeepEvents;
        var events = (ReadLog(logPath)?.Events ?? []).Where(e => e.At > cutoff).ToList();
        if (log.Resumed.Count > 0 || log.Paused.Count > 0) events.Add(new BackgroundEvent(log.RanAt, log.Resumed, log.Paused));
        log = log with { Events = events };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(log, Json), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return log;
    }

    public static BackgroundCheckLog? ReadLog(string logPath)
    {
        try
        {
            return File.Exists(logPath) ? JsonSerializer.Deserialize<BackgroundCheckLog>(File.ReadAllText(logPath), Json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
