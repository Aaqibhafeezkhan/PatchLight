using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class AppStateStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-state-" + Guid.NewGuid());
    private string FilePath => Path.Combine(dir, "app-state.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var state = new AppStateStore(FilePath).Load();
        Assert.Equal(HoldPolicy.DefaultDays, state.PauseDays);
        Assert.Empty(state.NotifiedProblems);
        Assert.Empty(state.NotifiedFixed);
    }

    [Fact]
    public void Saves_and_loads_back()
    {
        var store = new AppStateStore(FilePath);
        var state = new AppState(PauseDays: 7, NotifiedProblems: ["id-1"], NotifiedFixed: ["KB5127648"]);
        store.Save(state);
        Assert.Equal(state, store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("")]
    public void Damaged_file_gives_defaults(string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, content);
        Assert.Equal(new AppState(), new AppStateStore(FilePath).Load());
    }

    [Fact]
    public void Missing_fields_get_defaults()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, "{}");
        Assert.Equal(new AppState(), new AppStateStore(FilePath).Load());
    }

    [Theory]
    [InlineData(99, 35)]
    [InlineData(0, 1)]
    public void Pause_length_is_kept_in_range(int saved, int expected)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, $$"""{ "PauseDays": {{saved}} }""");
        Assert.Equal(expected, new AppStateStore(FilePath).Load().PauseDays);
    }
}
