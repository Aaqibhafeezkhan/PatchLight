using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PatchLight.Core;

// Reads the summary table at the top of a Windows release health page.
public static partial class ReleaseHealthParser
{
    [GeneratedRegex(@"KB\d{6,8}(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex KbPattern();

    // Fallback for a 7-digit KB glued straight onto a date with no separator in the HTML.
    [GeneratedRegex(@"KB(\d{7})(?=\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex GluedKbPattern();

    [GeneratedRegex(@"OS Build (\d{5}\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildPattern();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^(\d+)msg$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    public static IReadOnlyList<KnownIssue> Parse(string html, string pageUrl)
    {
        var document = new HtmlParser().ParseDocument(html);
        var table = document.QuerySelectorAll("table").FirstOrDefault(IsSummaryTable)
            ?? throw new ReleaseHealthFormatException(
                "Couldn't find the known issues list on Microsoft's page. The page may have changed.");

        var issues = new List<KnownIssue>();
        var rows = table.QuerySelectorAll("tbody tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 4) continue;

            var title = cells[0].QuerySelector("b")?.TextContent.Trim();
            if (string.IsNullOrEmpty(title)) continue;

            var idMatch = IdPattern().Match(cells[0].QuerySelector("div[id]")?.Id ?? "");
            var id = idMatch.Success ? idMatch.Groups[1].Value : NonSlug().Replace(title.ToLowerInvariant(), "-");

            var summary = cells[0].TextContent.Trim();
            if (summary.StartsWith(title, StringComparison.Ordinal)) summary = summary[title.Length..].Trim();

            var origin = cells[1].TextContent;
            var build = BuildPattern().Match(origin);
            var date = DatePattern().Match(origin);

            var statusText = cells[2].TextContent.Trim();
            var description = Description(document, id);

            issues.Add(new KnownIssue(
                Id: id,
                Title: title,
                Summary: summary,
                OriginatingKb: FindKb(cells[1]),
                OriginatingBuild: build.Success && Version.TryParse(build.Groups[1].Value, out var buildNumber)
                    ? buildNumber
                    : null,
                OriginatingDate: date.Success
                    && DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var originDate)
                    ? originDate
                    : null,
                Status: ParseStatus(statusText),
                ResolvedByKb: FindKb(cells[2]),
                Url: $"{pageUrl}#{id}msgdesc",
                Description: description,
                LikelyBusinessOnly: BusinessSignals.IsLikelyBusinessOnly(title, summary, description)));
        }

        // A table full of rows we can't read means the page layout changed; don't pretend nothing is wrong.
        if (rows.Length > 0 && issues.Count == 0)
            throw new ReleaseHealthFormatException(
                "Couldn't read the known issues list on Microsoft's page. The page may have changed.");
        return issues;
    }

    // The "Issue details" section below the table: an empty <div id="NNNNmsgdesc">, then a heading, a
    // status table and the paragraphs. The description is the first paragraph that isn't an "Updated" note.
    private static string Description(IDocument document, string id)
    {
        var anchor = document.GetElementById($"{id}msgdesc");
        for (var node = anchor?.NextElementSibling; node is not null; node = node.NextElementSibling)
        {
            if (node.Id?.EndsWith("msgdesc", StringComparison.Ordinal) == true || node.LocalName is "h2" or "h3") break;
            if (node.LocalName != "p") continue;
            var text = Clean(node.TextContent);
            if (text.Length > 0 && !text.StartsWith("Updated", StringComparison.OrdinalIgnoreCase)) return text;
        }
        return "";
    }

    private static string Clean(string text) =>
        Whitespace().Replace(text.Replace("​", ""), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static IssueStatus ParseStatus(string text)
    {
        var t = text.TrimStart();
        if (t.StartsWith("Resolved", StringComparison.OrdinalIgnoreCase)) return IssueStatus.Resolved;
        if (t.StartsWith("Mitigated", StringComparison.OrdinalIgnoreCase)) return IssueStatus.Mitigated;
        if (t.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase)) return IssueStatus.Confirmed;
        if (t.StartsWith("Investigating", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Reported", StringComparison.OrdinalIgnoreCase)) return IssueStatus.Investigating;
        return IssueStatus.Unknown;
    }

    // Child nodes are joined with spaces, so a <br> between a KB and a date doesn't run them together.
    private static string? FindKb(IElement cell)
    {
        var text = string.Join(" ", cell.ChildNodes.Select(node => node.TextContent));
        var kb = KbPattern().Match(text);
        if (kb.Success) return kb.Value.ToUpperInvariant();

        var glued = GluedKbPattern().Match(text);
        return glued.Success ? "KB" + glued.Groups[1].Value : null;
    }

    private static bool IsSummaryTable(IElement table)
    {
        var header = table.QuerySelector("thead")?.TextContent ?? "";
        return header.Contains("Summary", StringComparison.OrdinalIgnoreCase)
            && header.Contains("Originating update", StringComparison.OrdinalIgnoreCase);
    }
}
