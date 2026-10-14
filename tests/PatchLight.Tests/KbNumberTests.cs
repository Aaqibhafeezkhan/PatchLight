using PatchLight.Core;

namespace PatchLight.Tests;

public class KbNumberTests
{
    [Theory]
    [InlineData("KB5127648", "KB5127648")]
    [InlineData("kb5127648", "KB5127648")]
    [InlineData("5127648", "KB5127648")]
    [InlineData("  KB5127648 ", "KB5127648")]
    [InlineData("890830", "KB890830")]
    public void Accepts_common_ways_of_typing_a_kb(string input, string expected) =>
        Assert.Equal(expected, KbNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("KB12")]
    [InlineData("KB5127648x")]
    public void Rejects_anything_else(string? input) => Assert.Null(KbNumber.Normalize(input));
}
