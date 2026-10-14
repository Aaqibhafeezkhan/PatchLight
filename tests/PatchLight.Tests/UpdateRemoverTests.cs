using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.Tests;

public class UpdateRemoverTests
{
    // Shape of "dism /online /get-packages /english" output.
    private const string DismOutput = """

        Deployment Image Servicing and Management tool
        Version: 10.0.26100.5074

        Image Version: 10.0.26200.9711

        Packages listing:

        Package Identity : Package_for_DotNetRollup_481~31bf3856ad364e35~amd64~~10.0.9310.1
        State : Installed
        Release Type : Update
        Install Time : 10/1/2026 3:12 AM

        Package Identity : Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9550.1.10
        State : Superseded
        Release Type : Security Update
        Install Time : 9/9/2026 3:40 AM

        Package Identity : Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9711.1.6
        State : Installed
        Release Type : Security Update
        Install Time : 10/14/2026 3:02 AM

        Package Identity : Package_for_ServicingStack_9700~31bf3856ad364e35~amd64~~26100.9700.1.2
        State : Installed
        Release Type : Update
        Install Time : 10/14/2026 3:01 AM

        The operation completed successfully.
        """;

    private static InstalledUpdate Os(string kb, string build) =>
        new(kb, $"2026-10 Security Update ({kb}) ({build})", Version.Parse(build), DateTimeOffset.Now);

    [Fact]
    public void Finds_the_installed_cumulative_update_by_its_build()
    {
        Assert.Equal("Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9711.1.6",
            UpdateRemover.FindRollupPackage(DismOutput, Version.Parse("26200.9711")));
    }

    [Fact]
    public void A_replaced_cumulative_update_cannot_be_found()
    {
        Assert.Null(UpdateRemover.FindRollupPackage(DismOutput, Version.Parse("26200.9550")));
        Assert.Null(UpdateRemover.FindRollupPackage(DismOutput, Version.Parse("26200.1234")));
        Assert.Null(UpdateRemover.FindRollupPackage("", Version.Parse("26200.9711")));
    }

    [Fact]
    public void Windows_updates_use_dism_and_others_use_wusa()
    {
        Assert.Equal(RemovalMethod.Dism, UpdateRemover.MethodFor(Os("KB5127648", "26200.9711")));
        Assert.Equal(RemovalMethod.Wusa,
            UpdateRemover.MethodFor(new InstalledUpdate("KB5127690", "2026-10 .NET Framework Security Update (KB5127690)", null, DateTimeOffset.Now)));
    }

    [Theory]
    [InlineData(-2146498523, "0x800F0825")]
    [InlineData(5, "0x00000005")]
    public void Error_codes_read_like_windows_shows_them(int code, string text) =>
        Assert.Equal(text, UpdateRemover.ErrorCode(code));

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]   // done, restart needed
    [InlineData(5, false)]
    public void Exit_codes(int code, bool ok) => Assert.Equal(ok, UpdateRemover.Succeeded(code));
}
