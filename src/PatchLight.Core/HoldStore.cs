using System.Text.Json;

namespace PatchLight.Core;

public sealed class HoldStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<Hold> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Hold>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (JsonException)
        {
            // Keep the damaged file for inspection and start fresh.
            File.Move(path, path + ".bad", overwrite: true);
            return [];
        }
    }

    public void Save(IReadOnlyList<Hold> holds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(holds, Json));
        File.Move(temp, path, overwrite: true);
    }
}
