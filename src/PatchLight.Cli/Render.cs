using PatchLight.Core;
using Spectre.Console;

namespace PatchLight.Cli;

public static class Render
{
    public static string Badge(Light light) => light switch
    {
        Light.Green => "[green]● Safe[/]",
        Light.Yellow => "[yellow]● Wait[/]",
        _ => "[red]● Problems[/]",
    };

    public static void Check(IAnsiConsole console, CheckResult result, bool isAdmin)
    {
        if (result.Verdicts.Count == 0)
        {
            console.MarkupLine("[green]Nothing is waiting to install. You're up to date.[/]");
        }
        else
        {
            var table = new Table().RoundedBorder().AddColumn("Status").AddColumn("Update").AddColumn("Why");
            foreach (var v in result.Verdicts.OrderByDescending(v => v.Light))
            {
                var name = Markup.Escape(v.Update.Title) + (v.Update.IsHidden ? " [grey](paused)[/]" : "");
                table.AddRow(Badge(v.Light), name, Markup.Escape(v.Reason));
            }
            console.Write(table);
        }

        foreach (var v in result.Verdicts.Where(v => v.Issues.Count > 0))
        {
            console.WriteLine();
            console.MarkupLineInterpolated($"[bold]{v.Update.Kb ?? v.Update.Title}[/] known problems:");
            foreach (var issue in v.Issues)
            {
                console.MarkupLineInterpolated($"  • {issue.Title} [grey]({issue.Status})[/]");
                console.MarkupLineInterpolated($"    [grey]{issue.Url}[/]");
            }
        }

        var setAside = result.Verdicts.SelectMany(v => v.SetAside).DistinctBy(i => i.Id).ToList();
        if (setAside.Count > 0)
        {
            console.WriteLine();
            console.MarkupLine("[grey]Not counted, because these usually only affect business PCs:[/]");
            foreach (var issue in setAside)
                console.MarkupLineInterpolated($"[grey]  • {issue.Title}[/]");
        }

        foreach (var v in result.Verdicts.Where(v => v.Fixes.Count > 0))
            console.MarkupLineInterpolated($"[green]{v.Update.Kb} fixes {v.Fixes.Count} known problem(s).[/]");

        var toPause = result.Verdicts.Where(v => v.Light == Light.Red && !v.Update.IsHidden && v.Update.PauseKey is not null).ToList();
        if (toPause.Count > 0)
        {
            console.WriteLine();
            foreach (var v in toPause)
                console.MarkupLineInterpolated($"Tip: pause it with [bold]patchlight hold {v.Update.PauseKey}[/]");
            if (!isAdmin) console.MarkupLine("[grey](Pausing needs a terminal opened as administrator.)[/]");
        }

        foreach (var hold in result.ActiveHolds)
        {
            if (hold.IsWaiting)
                console.MarkupLineInterpolated($"Will pause {hold.Kb} when Windows offers it again (until {hold.ExpiresOn.ToLocalTime():MMM d}).");
            else
                console.MarkupLineInterpolated($"Paused: {hold.Kb} until {hold.ExpiresOn.ToLocalTime():MMM d}.");
        }

        foreach (var released in result.Released)
        {
            if (released.Reason == ReleaseReason.Gone)
                console.MarkupLineInterpolated($"Stopped pausing {released.Hold.Kb}: {Why(released.Reason)}.");
            else if (result.ReleaseApplied)
                console.MarkupLineInterpolated($"[green]Resumed {released.Hold.Kb}[/]: {Why(released.Reason)}. Windows Update will install it.");
            else
                console.MarkupLineInterpolated($"{released.Hold.Kb} is ready to resume ({Why(released.Reason)}). Run PatchLight as administrator to resume it.");
        }

        console.WriteLine();
        if (result.Data.FromCache)
            console.MarkupLineInterpolated($"[yellow]Couldn't reach Microsoft. Using the copy saved {StatusSummary.Ago(result.CheckedAt - result.Data.FetchedAt)}.[/]");
        else
            console.MarkupLine("[grey]Checked against Microsoft's known issues just now.[/]");
    }

    public static void Held(IAnsiConsole console, Hold hold) =>
        console.MarkupLineInterpolated(
            $"[green]Paused {hold.Kb}[/] until {hold.ExpiresOn.ToLocalTime():MMM d}. Run patchlight as administrator to resume it once Microsoft fixes it; after {hold.ExpiresOn.ToLocalTime():MMM d} it resumes on the next run.");

    public static void Uninstalled(IAnsiConsole console, Hold hold) =>
        console.MarkupLineInterpolated(
            $"[green]Removed {hold.Kb}.[/] Restart your PC to finish. When Windows offers it again, PatchLight will pause it until Microsoft fixes it (until {hold.ExpiresOn.ToLocalTime():MMM d} at the latest) the next time it runs as administrator.");

    public static void Released(IAnsiConsole console, ReleasedHold released) =>
        console.MarkupLineInterpolated($"[green]Resumed {released.Hold.Kb}[/]. Windows Update will install it.");

    public static void History(IAnsiConsole console, IReadOnlyList<HistoryItem> items)
    {
        if (items.Count == 0)
        {
            console.MarkupLine("No Windows updates were installed in this period.");
            return;
        }
        var table = new Table().RoundedBorder().AddColumn("Installed").AddColumn("Update").AddColumn("Known problems");
        foreach (var item in items)
        {
            var problems = item.Issues.Count == 0
                ? "[green]None[/]"
                : "[red]Likely cause:[/] " + Markup.Escape(string.Join("; ", item.Issues.Select(i => i.Title)));
            table.AddRow(item.Update.InstalledOn.ToLocalTime().ToString("MMM d, yyyy"), Markup.Escape(item.Update.Title), problems);
        }
        console.Write(table);
    }

    public static void Help(IAnsiConsole console)
    {
        console.MarkupLine("[bold]PatchLight[/] — know if a Windows update is safe before you install it.");
        console.WriteLine();
        console.MarkupLine("  [bold]patchlight[/]                      Check the updates waiting on this PC");
        console.MarkupLine($"  [bold]patchlight hold KB5127648[/]       Pause an update ({HoldPolicy.DefaultDays} days, --days N up to {HoldPolicy.MaxDays})");
        console.MarkupLine("  [bold]patchlight release KB5127648[/]    Resume a paused update");
        console.MarkupLine("  [bold]patchlight history[/]              Recent updates and which one likely broke something");
        console.MarkupLine("  [bold]patchlight uninstall KB5124008[/]  Remove an installed update and keep it paused until it's fixed");
        console.WriteLine();
        console.MarkupLine("[grey]Pausing, resuming and removing need a terminal opened as administrator.[/]");
    }

    private static string Why(ReleaseReason reason) => reason switch
    {
        ReleaseReason.Fixed => "Microsoft fixed its known problems",
        ReleaseReason.Expired => "the pause time ended",
        ReleaseReason.Gone => "Windows replaced it with a newer update",
        _ => "resumed by you",
    };
}
