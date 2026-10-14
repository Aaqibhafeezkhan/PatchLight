using System.Text.Json;
using PatchLight.Core;

namespace PatchLight.Windows;

// Paused: updates the protection settings paused on their own. Older logs don't have it.
public sealed record BackgroundCheckLog(DateTimeOffset RanAt, IReadOnlyList<string> Resumed, string? Error,
    IReadOnlyList<string>? Paused = null)
{
    public IReadOnlyList<string> Paused { get; init; } = Paused ?? [];

    public bool Equals(BackgroundCheckLog? other) =>
        other is not null && RanAt == other.RanAt && Error == other.Error && Resumed.SequenceEqual(other.Resumed)
        && Paused.SequenceEqual(other.Paused);

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
