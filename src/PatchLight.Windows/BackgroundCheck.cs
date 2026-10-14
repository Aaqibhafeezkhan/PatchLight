using System.Text.Json;
using PatchLight.Core;

namespace PatchLight.Windows;

public sealed record BackgroundCheckLog(DateTimeOffset RanAt, IReadOnlyList<string> Resumed, string? Error)
{
    public bool Equals(BackgroundCheckLog? other) =>
        other is not null && RanAt == other.RanAt && Error == other.Error && Resumed.SequenceEqual(other.Resumed);

    public override int GetHashCode() => HashCode.Combine(RanAt, Error);
}

// What the daily scheduled task does: resume fixed or expired pauses, then leave a note for the app.
public static class BackgroundCheck
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<BackgroundCheckLog> RunAsync(
        PatchLightService service, string logPath, TimeProvider time, CancellationToken ct)
    {
        BackgroundCheckLog log;
        try
        {
            var result = await service.CheckAsync(applyAutoRelease: true, ct);
            log = new BackgroundCheckLog(time.GetUtcNow(),
                result.Released.Where(r => r.Reason != ReleaseReason.Gone).Select(r => r.Hold.Kb).ToList(), null);
        }
        catch (Exception ex)
        {
            var message = ex is PatchLightException ? ex.Message : $"Something went wrong: {ex.GetType().Name}.";
            log = new BackgroundCheckLog(time.GetUtcNow(), [], message);
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
