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
        return match.Success && Version.TryParse(match.Groups[1].Value, out var build) ? build : null;
    }

    [GeneratedRegex(@"\bversion (\d{2}H\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    // "Windows 11, version 26H2" is an upgrade; "Cumulative Update for Windows 11, version 25H2 (KB...)" isn't.
    public static string? TryGetTargetVersion(string title) =>
        KbPattern().IsMatch(title) || BuildPattern().IsMatch(title) ? null : VersionIn(title);

    // For an update already known to be an upgrade, whatever else its title says.
    public static string? VersionIn(string title)
    {
        var match = VersionPattern().Match(title);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static string? TryGetKb(string title)
    {
        var match = KbPattern().Match(title);
        return match.Success ? KbNumber.Normalize(match.Value) : null;
    }
}
