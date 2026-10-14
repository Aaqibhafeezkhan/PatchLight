using PatchLight.Core;

namespace PatchLight.Tests;

public class UpdateTitleTests
{
    [Fact]
    public void Reads_build_and_kb_from_a_windows_update_title()
    {
        const string title = "2026-10 Security Update (KB5127648) (26200.9711)";
        Assert.Equal(new Version(26200, 9711), UpdateTitle.TryGetBuild(title));
        Assert.Equal("KB5127648", UpdateTitle.TryGetKb(title));
    }

    [Fact]
    public void Non_os_updates_have_no_build()
    {
        const string title = "Windows Malicious Software Removal Tool x64 - v5.146 (KB890830)";
        Assert.Null(UpdateTitle.TryGetBuild(title));
        Assert.Equal("KB890830", UpdateTitle.TryGetKb(title));
    }

    [Fact]
    public void Driver_titles_have_neither()
    {
        const string title = "Realtek - Net - 4/9/2015 12:00:00 AM - 9.1.409.2015";
        Assert.Null(UpdateTitle.TryGetBuild(title));
        Assert.Null(UpdateTitle.TryGetKb(title));
    }
 
    [Fact]
    public void Absurd_build_numbers_are_ignored() =>
        Assert.Null(UpdateTitle.TryGetBuild("x (26200.99999999999)"));
}
