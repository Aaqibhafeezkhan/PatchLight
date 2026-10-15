using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class ProtectionSettingsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-settings-" + Guid.NewGuid());
    private string FilePath => Path.Combine(dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Missing_file_means_auto_pause_on_and_no_wait()
    {
        var s = new ProtectionStore(FilePath).Load();
        Assert.True(s.AutoPauseRed);
        Assert.Equal(0, s.WaitDays);
        Assert.Equal(HoldPolicy.DefaultDays, s.PauseDays);
        Assert.False(s.OwnsDeferPolicy);
        Assert.Empty(s.LetThrough);
    }

    [Fact]
    public void Damaged_file_means_the_defaults()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, "{ nope");
        Assert.Equal(new ProtectionSettings(), new ProtectionStore(FilePath).Load());
    }

    [Fact]
    public void Saved_settings_read_back_the_same()
    {
        var store = new ProtectionStore(FilePath);
        var saved = new ProtectionSettings(true, 7, 21, true, [new LetThrough("KB5127648", ["5031", "4984"])]);
        store.Save(saved);
        Assert.Equal(saved, store.Load());
    }

    [Fact]
    public void Letting_an_update_through_replaces_what_was_known_about_it()
    {
        var s = new ProtectionSettings().WithLetThrough("KB5127648", ["1"]).WithLetThrough("KB5127001", []);
        s = s.WithLetThrough("KB5127648", ["1", "2"]);
        Assert.Equal([new LetThrough("KB5127001", []), new LetThrough("KB5127648", ["1", "2"])], s.LetThrough);
    }

    [Fact]
    public void Odd_values_are_brought_back_in_range()
    {
        Assert.Equal(0, new ProtectionSettings(WaitDays: 5).WaitDays);
        Assert.Equal(14, new ProtectionSettings(WaitDays: 14).WaitDays);
        Assert.Equal(HoldPolicy.MaxDays, new ProtectionSettings(PauseDays: 400).PauseDays);
        Assert.Equal(1, new ProtectionSettings(PauseDays: 0).PauseDays);
    }
}
