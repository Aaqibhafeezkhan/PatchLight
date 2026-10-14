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
            var holds = JsonSerializer.Deserialize<List<Hold?>>(File.ReadAllText(path), Json) ?? [];
            return holds.OfType<Hold>().ToList();
        }
        catch (JsonException)
        {
            // Keep the damaged file for inspection and start fresh.
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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
