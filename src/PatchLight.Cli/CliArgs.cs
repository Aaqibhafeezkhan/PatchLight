using PatchLight.Core;

namespace PatchLight.Cli;

public static class CliArgs
{
    public static int Days(string[] args, int fallback)
    {
        var i = Array.FindIndex(args, a => a.Equals("--days", StringComparison.OrdinalIgnoreCase));
        if (i < 0) return fallback;
        if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var days) || days < 1)
            throw new PatchLightException("--days needs a number of days, like --days 7.");
        return days;
    }

    public static string Required(string[] args, int index, string example) =>
        args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal)
            ? args[index]
            : throw new PatchLightException($"Missing update number. Example: patchlight {example}");
}
