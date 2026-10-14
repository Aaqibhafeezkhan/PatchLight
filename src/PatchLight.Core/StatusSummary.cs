namespace PatchLight.Core;

// The one-glance answer shown in the app header and the tray icon.
public static class StatusSummary
{
    // Paused updates can't hurt the PC, so they don't color the light.
    public static Light Overall(CheckResult result) =>
        result.Verdicts.Where(v => !v.Update.IsHidden).Select(v => v.Light).DefaultIfEmpty(Light.Green).Max();

    public static string Headline(Light light) => light switch
    {
        Light.Green => "All clear",
        Light.Yellow => "Wait a few days",
        _ => "Don't install yet",
    };

    public static string Detail(CheckResult result)
    {
        if (result.Verdicts.Count == 0) return "Nothing is waiting to install. You're up to date.";

        var overall = Overall(result);
        var count = result.Verdicts.Count(v => !v.Update.IsHidden && v.Light == overall);
        return overall switch
        {
            Light.Red when count == 1 => "1 update has known problems. Pause it until Microsoft fixes it.",
            Light.Red => $"{count} updates have known problems. Pause them until Microsoft fixes them.",
            Light.Yellow when count == 1 => "1 update has a known problem with a workaround or is brand new.",
            Light.Yellow => $"{count} updates have a known problem with a workaround or are brand new.",
            _ => "No known problems with the updates waiting on this PC.",
        };
    }

    public static string CheckedText(CheckResult result) =>
        result.Data.FromCache
            ? $"Couldn't reach Microsoft. Using the copy saved {Ago(result.CheckedAt - result.Data.FetchedAt)}."
            : "Checked against Microsoft's known issues just now.";

    public static string Ago(TimeSpan age) =>
        age.TotalHours < 1 ? "less than an hour ago"
        : age.TotalHours < 48 ? $"{(int)age.TotalHours} hours ago"
        : $"{(int)age.TotalDays} days ago";
}
