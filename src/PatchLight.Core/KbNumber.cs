using System.Text.RegularExpressions;

namespace PatchLight.Core;

public static partial class KbNumber
{
    [GeneratedRegex(@"^(?:KB)?(\d{6,8})$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static string? Normalize(string? text)
    {
        if (text is null) return null;
        var match = Pattern().Match(text.Trim());
        return match.Success ? "KB" + match.Groups[1].Value : null;
    }
}
