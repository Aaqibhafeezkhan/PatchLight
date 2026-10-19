using System.Text.Json;

namespace PatchLight.Core;

// Per-user app settings and which alerts were already shown. Older or partial files still load.
public sealed record AppState(
    int PauseDays = HoldPolicy.DefaultDays,
    IReadOnlyList<string>? NotifiedProblems = null,
    IReadOnlyList<string>? NotifiedFixed = null,
    DateTimeOffset? SeenBackgroundRun = null,
    bool? CountBusinessIssues = null,
    bool CheckNews = true,
    string? NotifiedAppVersion = null)
{
    public int PauseDays { get; init; } = Math.Clamp(PauseDays, 1, HoldPolicy.MaxDays);
    public IReadOnlyList<string> NotifiedProblems { get; init; } = NotifiedProblems ?? [];
    public IReadOnlyList<string> NotifiedFixed { get; init; } = NotifiedFixed ?? [];

    public bool Equals(AppState? other) =>
        other is not null && PauseDays == other.PauseDays && SeenBackgroundRun == other.SeenBackgroundRun
        && CountBusinessIssues == other.CountBusinessIssues && CheckNews == other.CheckNews
        && NotifiedAppVersion == other.NotifiedAppVersion
        && NotifiedProblems.SequenceEqual(other.NotifiedProblems) && NotifiedFixed.SequenceEqual(other.NotifiedFixed);

    public override int GetHashCode() => PauseDays;
}

public sealed class AppStateStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public AppState Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }
}
