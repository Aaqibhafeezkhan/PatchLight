using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class HoldStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "holds.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_means_no_holds() => Assert.Empty(new HoldStore(FilePath).Load());

    [Fact]
    public void Saves_and_loads_holds()
    {
        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var hold = new Hold("KB5127648", "abc-123", now, now.AddDays(14), "Paused by you");
        var store = new HoldStore(FilePath);

        store.Save([hold]);

        Assert.Equal(hold, Assert.Single(new HoldStore(FilePath).Load()));
    }

    [Fact]
    public void Damaged_file_is_kept_aside_and_does_not_crash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(new HoldStore(FilePath).Load());
        Assert.True(File.Exists(FilePath + ".bad"));
        Assert.False(File.Exists(FilePath));
    }
}
