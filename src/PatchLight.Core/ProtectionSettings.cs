using System.Text.Json;

namespace PatchLight.Core;

// An update the person let through (resumed by hand, or its pause ran out) with the problems it had then.
// Auto-pause leaves it alone until it gets a problem that isn't on this list.
public sealed record LetThrough(string Kb, IReadOnlyList<string> IssueIds)
{
    public bool Equals(LetThrough? other) => other is not null && Kb == other.Kb && IssueIds.SequenceEqual(other.IssueIds);
    public override int GetHashCode() => Kb.GetHashCode();
}

// What the background check does on its own, for everyone on this PC. Only administrators can change it.
// WaitDays: how old a new Windows update must be before Windows may install it (0 = off).
// OwnsDeferPolicy: PatchLight wrote Windows' own deferral policy, so it may remove it again.
public sealed record ProtectionSettings(
    bool AutoPauseRed = false,
    int WaitDays = 0,
    int PauseDays = HoldPolicy.DefaultDays,
    bool OwnsDeferPolicy = false,
    IReadOnlyList<LetThrough>? LetThrough = null)
{
    public static readonly int[] WaitChoices = [0, 3, 7, 14];

    public int WaitDays { get; init; } = WaitChoices.Contains(WaitDays) ? WaitDays : 0;
    public int PauseDays { get; init; } = Math.Clamp(PauseDays, 1, HoldPolicy.MaxDays);
    public IReadOnlyList<LetThrough> LetThrough { get; init; } = LetThrough ?? [];

    public bool Equals(ProtectionSettings? other) =>
        other is not null && AutoPauseRed == other.AutoPauseRed && WaitDays == other.WaitDays && PauseDays == other.PauseDays
        && OwnsDeferPolicy == other.OwnsDeferPolicy && LetThrough.SequenceEqual(other.LetThrough);

    public override int GetHashCode() => HashCode.Combine(AutoPauseRed, WaitDays, PauseDays, OwnsDeferPolicy);
}

public sealed class ProtectionStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Path => path;

    // A missing or damaged file means nothing is turned on.
    public ProtectionSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ProtectionSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(ProtectionSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, path, overwrite: true);
    }
}
