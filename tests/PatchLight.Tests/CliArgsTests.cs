using PatchLight.Cli;
using PatchLight.Core;

namespace PatchLight.Tests;

public class CliArgsTests
{
    [Fact]
    public void Days_defaults_when_missing() => Assert.Equal(14, CliArgs.Days(["hold", "KB5127648"], 14));

    [Fact]
    public void Days_reads_the_number() => Assert.Equal(7, CliArgs.Days(["hold", "KB5127648", "--days", "7"], 14));

    [Theory]
    [InlineData("--days")]
    [InlineData("--days", "soon")]
    [InlineData("--days", "0")]
    public void Days_rejects_bad_values(params string[] tail)
    {
        var ex = Assert.Throws<PatchLightException>(() => CliArgs.Days(["hold", "KB5127648", .. tail], 14));
        Assert.Contains("--days 7", ex.Message);
    }

    [Fact]
    public void Required_returns_the_argument() =>
        Assert.Equal("KB5127648", CliArgs.Required(["hold", "KB5127648"], 1, "hold KB5127648"));

    [Fact]
    public void Required_explains_what_is_missing()
    {
        var ex = Assert.Throws<PatchLightException>(() => CliArgs.Required(["hold"], 1, "hold KB5127648"));
        Assert.Contains("patchlight hold KB5127648", ex.Message);
    }

    [Theory]
    [InlineData("3651")]
    [InlineData("99999")]
    public void Days_rejects_absurdly_long_periods(string value)
    {
        var ex = Assert.Throws<PatchLightException>(() => CliArgs.Days(["history", "--days", value], 60));
        Assert.Contains("--days 7", ex.Message);
        Assert.Contains("3650", ex.Message);
    }

    [Fact]
    public void Days_allows_the_maximum() => Assert.Equal(3650, CliArgs.Days(["history", "--days", "3650"], 60));
}
