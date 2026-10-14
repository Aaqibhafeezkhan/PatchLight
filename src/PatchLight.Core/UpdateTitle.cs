using System.Text.RegularExpressions;

namespace PatchLight.Core;

public static partial class UpdateTitle
{
    [GeneratedRegex(@"\((\d{5}\.\d+)\)")]
    private static partial Regex BuildPattern();

    [GeneratedRegex(@"KB\d{6,8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex KbPattern();

    // OS updates end with "(26200.9711)"; drivers and Defender updates don't.
    public static Version? TryGetBuild(string title)
    {
        var match = BuildPattern().Match(title);
        return match.Success ? Version.Parse(match.Groups[1].Value) : null;
    }

    public static string? TryGetKb(string title)
    {
        var match = KbPattern().Match(title);
        return match.Success ? KbNumber.Normalize(match.Value) : null;
    }
}
