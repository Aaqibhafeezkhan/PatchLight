# PatchLight Engine + CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the PatchLight engine and the `patchlight` command: it lists pending Windows updates with a 🟢🟡🔴 light based on Microsoft's known issues, pauses/resumes bad ones, and points at the update that likely broke the PC.

**Architecture:** A pure `PatchLight.Core` library (parsing, lights, holds, orchestration — fully unit-tested with fakes) sits under a thin `PatchLight.Windows` layer (Windows Update COM API + registry) and a thin `PatchLight.Cli` exe (Spectre.Console output). The future WinUI app (Plan 2) reuses Core + Windows unchanged.

**Tech Stack:** .NET 10, C#, AngleSharp (HTML parsing), Spectre.Console (terminal UI), xUnit, Microsoft.Extensions.TimeProvider.Testing, Spectre.Console.Testing.

**Spec:** `docs/superpowers/specs/2026-10-13-patchlight-design.md`

## Global Constraints

- Windows 11 only: detect with registry `CurrentBuild >= 22000` (never trust `ProductName`, it says "Windows 10" on Windows 11).
- All user-facing text in English, plain everyday words, no jargon where a simple word works.
- No paid services and no server: each PC fetches `https://learn.microsoft.com/en-us/windows/release-health/status-windows-11-<version lowercase>` itself.
- PatchLight only hides/unhides updates in this plan. It never installs or uninstalls anything.
- Holds: default 14 days, max 35 days, auto-resume when the update turns green, the hold expires, or the update is no longer offered.
- Never show a green light without known-issue data: if Microsoft can't be reached and no saved copy exists, the command fails with a clear message.
- App data lives in `%ProgramData%\PatchLight` (`holds.json`, `cache\`).
- Target framework `net10.0` for Core, `net10.0-windows` for Windows/Cli/Tests. Warnings are errors.
- License: MIT.

## Review Focus

1. **Microsoft unreachable or page layout changed** — the user expects the saved copy to be used with its age shown, and a clear error (not a crash, not a green light) when there is no copy. Pinned in Task 5.
2. **Pause/resume without administrator rights** — the user expects "run as administrator" in plain words, and no hold recorded when hiding failed. Pinned in Task 6 (service) and Task 8 (CLI).
3. **KB typed in different forms** (`5127648`, `kb5127648`, ` KB5127648 `) — accepted; junk like `hello` rejected with an example. Pinned in Task 1.
4. **Held update replaced by next month's update** — the hold is quietly dropped, no error. Pinned in Task 6.
5. **Damaged `holds.json`** — the app keeps working, the bad file is kept as `holds.json.bad`. Pinned in Task 4.

---

## File Structure

```
PatchLight/
  PatchLight.slnx
  Directory.Build.props            shared compiler settings
  .gitignore  LICENSE  README.md
  src/PatchLight.Core/             net10.0, no Windows APIs
    KbNumber.cs                    normalize "5127648" -> "KB5127648"
    UpdateTitle.cs                 pull KB / OS build out of update titles
    Models.cs                      records + enums shared by everything
    PatchLightException.cs         user-facing error type
    ReleaseHealthParser.cs         Microsoft HTML -> KnownIssue list
    VerdictEngine.cs               update + issues -> light + reason
    HoldStore.cs                   holds.json read/write
    HoldPolicy.cs                  14 / 35 day constants
    IssueSource.cs                 IIssueSource + ReleaseHealthSource (download + cache)
    IUpdateClient.cs               what Core needs from Windows Update
    PatchLightService.cs           check / hold / release / history
  src/PatchLight.Windows/          net10.0-windows
    WindowsUpdateClient.cs         COM Windows Update Agent
    SystemInfo.cs                  Windows version, admin check, data folder
  src/PatchLight.Cli/              net10.0-windows exe "patchlight"
    Program.cs  CliApp.cs  CliArgs.cs  Render.cs
  tests/PatchLight.Tests/          net10.0-windows xUnit
    Fixtures/status-windows-11-25h2.html   (already saved 2026-10-13 — do not re-download)
    KbNumberTests.cs  UpdateTitleTests.cs  ReleaseHealthParserTests.cs  VerdictEngineTests.cs
    HoldStoreTests.cs  ReleaseHealthSourceTests.cs  PatchLightServiceTests.cs
    Fakes.cs  WindowsIntegrationTests.cs  CliArgsTests.cs  RenderTests.cs
```

All commands below run from `C:\Users\Admin\Documents\PatchLight` in PowerShell.

---

### Task 1: Toolchain, solution skeleton, KB + title helpers

**Files:**
- Create: `PatchLight.slnx`, `Directory.Build.props`, `.gitignore`, `src/PatchLight.Core/PatchLight.Core.csproj`, `src/PatchLight.Core/KbNumber.cs`, `src/PatchLight.Core/UpdateTitle.cs`, `tests/PatchLight.Tests/PatchLight.Tests.csproj`
- Test: `tests/PatchLight.Tests/KbNumberTests.cs`, `tests/PatchLight.Tests/UpdateTitleTests.cs`

**Interfaces:**
- Produces: `static string? KbNumber.Normalize(string? text)` → `"KB5127648"` or `null`; `static Version? UpdateTitle.TryGetBuild(string title)`; `static string? UpdateTitle.TryGetKb(string title)`.

- [ ] **Step 1: Install the .NET 10 SDK and check it**

```powershell
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
dotnet --list-sdks
```
Expected: a line starting with `10.`. (Open a new terminal if `dotnet` is not found.)

- [ ] **Step 2: Create repo, solution and projects**

```powershell
git init
dotnet new gitignore
dotnet new sln -n PatchLight
dotnet new classlib -n PatchLight.Core -o src/PatchLight.Core -f net10.0
dotnet new xunit -n PatchLight.Tests -o tests/PatchLight.Tests -f net10.0
dotnet sln add src/PatchLight.Core tests/PatchLight.Tests
dotnet add tests/PatchLight.Tests reference src/PatchLight.Core
Remove-Item src/PatchLight.Core/Class1.cs, tests/PatchLight.Tests/UnitTest1.cs
```

Edit `tests/PatchLight.Tests/PatchLight.Tests.csproj`: change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>` and add inside `<Project>`:

```xml
  <ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the failing tests**

`tests/PatchLight.Tests/KbNumberTests.cs`:

```csharp
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
```

`tests/PatchLight.Tests/UpdateTitleTests.cs`:

```csharp
using PatchLight.Core;

namespace PatchLight.Tests;

public class UpdateTitleTests
{
    [Fact]
    public void Reads_build_and_kb_from_a_windows_update_title()
    {
        const string title = "2026-10 Security Update (KB5127648) (26200.9711)";
        Assert.Equal(new Version(26200, 9711), UpdateTitle.TryGetBuild(title));
        Assert.Equal("KB5127648", UpdateTitle.TryGetKb(title));
    }

    [Fact]
    public void Non_os_updates_have_no_build()
    {
        const string title = "Windows Malicious Software Removal Tool x64 - v5.146 (KB890830)";
        Assert.Null(UpdateTitle.TryGetBuild(title));
        Assert.Equal("KB890830", UpdateTitle.TryGetKb(title));
    }

    [Fact]
    public void Driver_titles_have_neither() 
    {
        const string title = "Realtek - Net - 4/9/2015 12:00:00 AM - 9.1.409.2015";
        Assert.Null(UpdateTitle.TryGetBuild(title));
        Assert.Null(UpdateTitle.TryGetKb(title));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS with `The name 'KbNumber' does not exist in the current context`.

- [ ] **Step 5: Implement**

`src/PatchLight.Core/KbNumber.cs`:

```csharp
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
```

`src/PatchLight.Core/UpdateTitle.cs`:

```csharp
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
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS, 13 tests.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "chore: solution skeleton, KB and update title helpers"
```

---

### Task 2: Models and the Microsoft known-issues parser

**Files:**
- Create: `src/PatchLight.Core/Models.cs`, `src/PatchLight.Core/PatchLightException.cs`, `src/PatchLight.Core/ReleaseHealthParser.cs`
- Test: `tests/PatchLight.Tests/ReleaseHealthParserTests.cs` (uses the existing fixture `tests/PatchLight.Tests/Fixtures/status-windows-11-25h2.html`, a snapshot taken 2026-10-13 — never refresh it, the tests depend on its content)

**Interfaces:**
- Consumes: nothing.
- Produces (exact, used by every later task):

```csharp
public enum IssueStatus { Investigating, Confirmed, Mitigated, Resolved, Unknown }
public enum Light { Green, Yellow, Red }   // order matters: sorting descending puts Red first
public sealed record KnownIssue(string Id, string Title, string Summary, string? OriginatingKb,
    Version? OriginatingBuild, DateOnly? OriginatingDate, IssueStatus Status, string? ResolvedByKb, string Url);
public sealed record PendingUpdate(string UpdateId, string? Kb, string Title, Version? Build,
    DateOnly? Released, bool IsHidden) { public bool IsOsUpdate => Build is not null; }
public sealed record InstalledUpdate(string? Kb, string Title, Version? Build, DateTimeOffset InstalledOn);
public sealed record UpdateVerdict(PendingUpdate Update, Light Light, string Reason,
    IReadOnlyList<KnownIssue> Issues, IReadOnlyList<KnownIssue> Fixes);
public sealed record Hold(string Kb, string UpdateId, DateTimeOffset HeldOn, DateTimeOffset ExpiresOn, string Reason);
public enum ReleaseReason { Fixed, Expired, Gone, Manual }
public sealed record ReleasedHold(Hold Hold, ReleaseReason Reason);
public sealed record IssueSnapshot(IReadOnlyList<KnownIssue> Issues, DateTimeOffset FetchedAt, bool FromCache);
public sealed record CheckResult(IReadOnlyList<UpdateVerdict> Verdicts, IReadOnlyList<Hold> ActiveHolds,
    IReadOnlyList<ReleasedHold> Released, bool ReleaseApplied, IssueSnapshot Data, DateTimeOffset CheckedAt);
public sealed record HistoryItem(InstalledUpdate Update, IReadOnlyList<KnownIssue> Issues);
public class PatchLightException(string message) : Exception(message);
public sealed class ReleaseHealthFormatException(string message) : PatchLightException(message);
static IReadOnlyList<KnownIssue> ReleaseHealthParser.Parse(string html, string pageUrl);
static IssueStatus ReleaseHealthParser.ParseStatus(string text);
```

- [ ] **Step 1: Write the failing test**

`tests/PatchLight.Tests/ReleaseHealthParserTests.cs`:

```csharp
using PatchLight.Core;

namespace PatchLight.Tests;

public class ReleaseHealthParserTests
{
    public const string Url = "https://learn.microsoft.com/en-us/windows/release-health/status-windows-11-25h2";

    public static string FixtureHtml() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "status-windows-11-25h2.html"));

    private static IReadOnlyList<KnownIssue> Parse() => ReleaseHealthParser.Parse(FixtureHtml(), Url);

    [Fact]
    public void Reads_every_row_of_the_summary_table() => Assert.Equal(11, Parse().Count);

    [Fact]
    public void Reads_an_open_issue()
    {
        var issue = Parse().Single(i => i.Id == "5031");
        Assert.Equal("Remote Desktop connections might fail for non-admin users with error 0xC000000D", issue.Title);
        Assert.StartsWith("This issue affects non-admin users", issue.Summary);
        Assert.Equal("KB5124010", issue.OriginatingKb);
        Assert.Equal(new Version(26200, 9550), issue.OriginatingBuild);
        Assert.Equal(new DateOnly(2026, 9, 22), issue.OriginatingDate);
        Assert.Equal(IssueStatus.Mitigated, issue.Status);
        Assert.Null(issue.ResolvedByKb);
        Assert.Equal(Url + "#5031msgdesc", issue.Url);
    }

    [Fact]
    public void Reads_which_update_resolved_an_issue()
    {
        var issue = Parse().Single(i => i.Id == "5006");
        Assert.Equal(IssueStatus.Resolved, issue.Status);
        Assert.Equal("KB5127648", issue.ResolvedByKb);
        Assert.Equal("KB5120998", issue.OriginatingKb);
    }

    [Fact]
    public void Handles_issues_without_an_originating_update()
    {
        var issue = Parse().Single(i => i.Id == "4956");
        Assert.Null(issue.OriginatingKb);
        Assert.Null(issue.OriginatingBuild);
        Assert.Null(issue.OriginatingDate);
        Assert.Equal(IssueStatus.Resolved, issue.Status);
    }

    [Fact]
    public void Throws_a_clear_error_when_the_table_is_missing()
    {
        var ex = Assert.Throws<ReleaseHealthFormatException>(() =>
            ReleaseHealthParser.Parse("<html><body><p>Page moved</p></body></html>", Url));
        Assert.Contains("known issues", ex.Message);
    }

    [Theory]
    [InlineData("Resolved KB5127648", IssueStatus.Resolved)]
    [InlineData("Mitigated", IssueStatus.Mitigated)]
    [InlineData("Mitigated External", IssueStatus.Mitigated)]
    [InlineData("Confirmed", IssueStatus.Confirmed)]
    [InlineData("Investigating", IssueStatus.Investigating)]
    [InlineData("Reported", IssueStatus.Investigating)]
    [InlineData("Something new", IssueStatus.Unknown)]
    public void Maps_status_text(string text, IssueStatus expected) =>
        Assert.Equal(expected, ReleaseHealthParser.ParseStatus(text));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ReleaseHealthParserTests`
Expected: build FAILS with `The type or namespace name 'KnownIssue' could not be found`.

- [ ] **Step 3: Implement**

```powershell
dotnet add src/PatchLight.Core package AngleSharp
```

`src/PatchLight.Core/PatchLightException.cs`:

```csharp
namespace PatchLight.Core;

// Message is shown to the user as-is, so keep it plain English.
public class PatchLightException(string message) : Exception(message);

public sealed class ReleaseHealthFormatException(string message) : PatchLightException(message);
```

`src/PatchLight.Core/Models.cs`:

```csharp
namespace PatchLight.Core;

public enum IssueStatus { Investigating, Confirmed, Mitigated, Resolved, Unknown }

// Order matters: sorting descending puts Red first.
public enum Light { Green, Yellow, Red }

public sealed record KnownIssue(
    string Id,
    string Title,
    string Summary,
    string? OriginatingKb,
    Version? OriginatingBuild,
    DateOnly? OriginatingDate,
    IssueStatus Status,
    string? ResolvedByKb,
    string Url);

public sealed record PendingUpdate(
    string UpdateId,
    string? Kb,
    string Title,
    Version? Build,
    DateOnly? Released,
    bool IsHidden)
{
    public bool IsOsUpdate => Build is not null;
}

public sealed record InstalledUpdate(string? Kb, string Title, Version? Build, DateTimeOffset InstalledOn);

public sealed record UpdateVerdict(
    PendingUpdate Update,
    Light Light,
    string Reason,
    IReadOnlyList<KnownIssue> Issues,
    IReadOnlyList<KnownIssue> Fixes);

public sealed record Hold(string Kb, string UpdateId, DateTimeOffset HeldOn, DateTimeOffset ExpiresOn, string Reason);

public enum ReleaseReason { Fixed, Expired, Gone, Manual }

public sealed record ReleasedHold(Hold Hold, ReleaseReason Reason);

public sealed record IssueSnapshot(IReadOnlyList<KnownIssue> Issues, DateTimeOffset FetchedAt, bool FromCache);

public sealed record CheckResult(
    IReadOnlyList<UpdateVerdict> Verdicts,
    IReadOnlyList<Hold> ActiveHolds,
    IReadOnlyList<ReleasedHold> Released,
    bool ReleaseApplied,
    IssueSnapshot Data,
    DateTimeOffset CheckedAt);

public sealed record HistoryItem(InstalledUpdate Update, IReadOnlyList<KnownIssue> Issues);
```

`src/PatchLight.Core/ReleaseHealthParser.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PatchLight.Core;

// Reads the summary table at the top of a Windows release health page.
public static partial class ReleaseHealthParser
{
    [GeneratedRegex(@"KB\d{6,8}", RegexOptions.IgnoreCase)]
    private static partial Regex KbPattern();

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
        foreach (var row in table.QuerySelectorAll("tbody tr"))
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
            var kb = KbPattern().Match(origin);
            var build = BuildPattern().Match(origin);
            var date = DatePattern().Match(origin);

            var statusText = cells[2].TextContent.Trim();
            var resolvedBy = KbPattern().Match(statusText);

            issues.Add(new KnownIssue(
                Id: id,
                Title: title,
                Summary: summary,
                OriginatingKb: kb.Success ? kb.Value.ToUpperInvariant() : null,
                OriginatingBuild: build.Success ? Version.Parse(build.Groups[1].Value) : null,
                OriginatingDate: date.Success
                    ? DateOnly.ParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null,
                Status: ParseStatus(statusText),
                ResolvedByKb: resolvedBy.Success ? resolvedBy.Value.ToUpperInvariant() : null,
                Url: $"{pageUrl}#{id}msgdesc"));
        }
        return issues;
    }

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

    private static bool IsSummaryTable(IElement table)
    {
        var header = table.QuerySelector("thead")?.TextContent ?? "";
        return header.Contains("Summary", StringComparison.OrdinalIgnoreCase)
            && header.Contains("Originating update", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ReleaseHealthParserTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: parse Microsoft release health known issues"
```

---

### Task 3: Verdict engine (the traffic light)

**Files:**
- Create: `src/PatchLight.Core/VerdictEngine.cs`
- Test: `tests/PatchLight.Tests/VerdictEngineTests.cs`

**Interfaces:**
- Consumes: `KnownIssue`, `PendingUpdate`, `UpdateVerdict`, `Light`, `IssueStatus` (Task 2).
- Produces: `static UpdateVerdict VerdictEngine.Judge(PendingUpdate update, IReadOnlyList<KnownIssue> issues, DateOnly today)`; `static bool VerdictEngine.Affects(KnownIssue issue, string? kb, Version? build)`; `const int VerdictEngine.NewUpdateWaitDays = 3`.

Rules: open issue = status not `Resolved`. An issue affects an update if the KB matches the originating KB, or the update is an OS update on the same build line (same `Major`) with build ≥ originating build (cumulative updates inherit problems). Red if any affecting issue is `Confirmed`/`Investigating`; Yellow if any other affecting issue (`Mitigated`/`Unknown`); Yellow if an OS update was released less than 3 days ago; else Green. Non-OS updates (drivers, Defender definitions) never get the "too new" yellow — Defender ships daily.

- [ ] **Step 1: Write the failing test**

`tests/PatchLight.Tests/VerdictEngineTests.cs`:

```csharp
using PatchLight.Core;

namespace PatchLight.Tests;

public class VerdictEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    public static KnownIssue Issue(IssueStatus status, string? kb = "KB5124010", string? build = "26200.9550",
        string? fixedBy = null, string title = "Remote Desktop might fail") =>
        new("1", title, "", kb, build is null ? null : Version.Parse(build), new DateOnly(2026, 9, 22),
            status, fixedBy, "https://example.test/#1msgdesc");

    public static PendingUpdate Update(string kb = "KB5127648", string? build = "26200.9711",
        DateOnly? released = null, bool hidden = false) =>
        new("id-" + kb, kb, $"2026-10 Security Update ({kb})", build is null ? null : Version.Parse(build),
            released ?? new DateOnly(2026, 10, 1), hidden);

    [Fact]
    public void Open_issue_from_the_same_update_is_red()
    {
        var v = VerdictEngine.Judge(Update("KB5124010", "26200.9550"), [Issue(IssueStatus.Confirmed)], Today);
        Assert.Equal(Light.Red, v.Light);
        Assert.Equal("Known problem: Remote Desktop might fail", v.Reason);
        Assert.Single(v.Issues);
    }

    [Fact]
    public void Later_cumulative_update_inherits_an_open_issue()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Investigating)], Today);
        Assert.Equal(Light.Red, v.Light);
    }

    [Fact]
    public void Older_build_is_not_affected()
    {
        var v = VerdictEngine.Judge(Update("KB5124008", "26200.9445"), [Issue(IssueStatus.Confirmed)], Today);
        Assert.Equal(Light.Green, v.Light);
        Assert.Equal("No known problems.", v.Reason);
    }

    [Fact]
    public void Different_build_line_is_not_affected()
    {
        var v = VerdictEngine.Judge(Update(build: "26300.100"), [Issue(IssueStatus.Confirmed, kb: null)], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Theory]
    [InlineData(IssueStatus.Mitigated)]
    [InlineData(IssueStatus.Unknown)]
    public void Issue_with_workaround_or_unclear_status_is_yellow(IssueStatus status)
    {
        var v = VerdictEngine.Judge(Update(), [Issue(status)], Today);
        Assert.Equal(Light.Yellow, v.Light);
        Assert.Equal("Known problem with a workaround: Remote Desktop might fail", v.Reason);
    }

    [Fact]
    public void Resolved_issue_is_ignored()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Resolved)], Today);
        Assert.Equal(Light.Green, v.Light);
        Assert.Empty(v.Issues);
    }

    [Fact]
    public void Red_wins_over_yellow_and_counts_the_rest()
    {
        var v = VerdictEngine.Judge(Update(),
            [Issue(IssueStatus.Mitigated, title: "Audio"), Issue(IssueStatus.Confirmed, title: "Black screen")], Today);
        Assert.Equal(Light.Red, v.Light);
        Assert.Equal("Known problem: Black screen (+1 more)", v.Reason);
        Assert.Equal(2, v.Issues.Count);
    }

    [Theory]
    [InlineData(0, "Released today. Wait a few days in case problems show up.")]
    [InlineData(1, "Released 1 day ago. Wait a few days in case problems show up.")]
    [InlineData(2, "Released 2 days ago. Wait a few days in case problems show up.")]
    public void Brand_new_os_update_is_yellow(int daysOld, string reason)
    {
        var v = VerdictEngine.Judge(Update(released: Today.AddDays(-daysOld)), [], Today);
        Assert.Equal(Light.Yellow, v.Light);
        Assert.Equal(reason, v.Reason);
    }

    [Fact]
    public void Three_day_old_os_update_is_green()
    {
        var v = VerdictEngine.Judge(Update(released: Today.AddDays(-3)), [], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Fact]
    public void Brand_new_non_os_update_is_green()
    {
        var v = VerdictEngine.Judge(Update("KB2267602", build: null, released: Today), [], Today);
        Assert.Equal(Light.Green, v.Light);
    }

    [Fact]
    public void Lists_issues_the_update_fixes()
    {
        var v = VerdictEngine.Judge(Update(), [Issue(IssueStatus.Resolved, fixedBy: "KB5127648")], Today);
        Assert.Single(v.Fixes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter VerdictEngineTests`
Expected: build FAILS with `The name 'VerdictEngine' does not exist`.

- [ ] **Step 3: Implement**

`src/PatchLight.Core/VerdictEngine.cs`:

```csharp
namespace PatchLight.Core;

public static class VerdictEngine
{
    public const int NewUpdateWaitDays = 3;

    public static UpdateVerdict Judge(PendingUpdate update, IReadOnlyList<KnownIssue> issues, DateOnly today)
    {
        var affecting = issues
            .Where(i => i.Status != IssueStatus.Resolved && Affects(i, update.Kb, update.Build))
            .ToList();
        var fixes = update.Kb is null
            ? []
            : issues.Where(i => i.ResolvedByKb == update.Kb).ToList();

        var serious = affecting.Where(i => i.Status is IssueStatus.Confirmed or IssueStatus.Investigating).ToList();
        if (serious.Count > 0)
            return new(update, Light.Red, Describe("Known problem", serious, affecting.Count), affecting, fixes);

        if (affecting.Count > 0)
            return new(update, Light.Yellow, Describe("Known problem with a workaround", affecting, affecting.Count),
                affecting, fixes);

        if (update.IsOsUpdate && update.Released is { } released)
        {
            var age = today.DayNumber - released.DayNumber;
            if (age < NewUpdateWaitDays)
            {
                var when = age <= 0 ? "Released today." : age == 1 ? "Released 1 day ago." : $"Released {age} days ago.";
                return new(update, Light.Yellow, $"{when} Wait a few days in case problems show up.", affecting, fixes);
            }
        }

        return new(update, Light.Green, "No known problems.", affecting, fixes);
    }

    // Cumulative OS updates carry every change from earlier builds on the same line,
    // so an open issue from build 26200.9550 also affects 26200.9711.
    public static bool Affects(KnownIssue issue, string? kb, Version? build)
    {
        if (kb is not null && issue.OriginatingKb == kb) return true;
        return build is not null
            && issue.OriginatingBuild is not null
            && build.Major == issue.OriginatingBuild.Major
            && build >= issue.OriginatingBuild;
    }

    private static string Describe(string label, List<KnownIssue> lead, int total) =>
        total > 1 ? $"{label}: {lead[0].Title} (+{total - 1} more)" : $"{label}: {lead[0].Title}";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter VerdictEngineTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: traffic light verdicts for pending updates"
```

---

### Task 4: Hold storage

**Files:**
- Create: `src/PatchLight.Core/HoldStore.cs`, `src/PatchLight.Core/HoldPolicy.cs`
- Test: `tests/PatchLight.Tests/HoldStoreTests.cs`

**Interfaces:**
- Consumes: `Hold` (Task 2).
- Produces: `sealed class HoldStore(string path)` with `IReadOnlyList<Hold> Load()` and `void Save(IReadOnlyList<Hold> holds)`; `static class HoldPolicy { const int DefaultDays = 14; const int MaxDays = 35; }`.

- [ ] **Step 1: Write the failing test**

`tests/PatchLight.Tests/HoldStoreTests.cs`:

```csharp
using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class HoldStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "holds.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_means_no_holds() => Assert.Empty(new HoldStore(FilePath).Load());

    [Fact]
    public void Saves_and_loads_holds()
    {
        var now = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var hold = new Hold("KB5127648", "abc-123", now, now.AddDays(14), "Paused by you");
        var store = new HoldStore(FilePath);

        store.Save([hold]);

        Assert.Equal(hold, Assert.Single(new HoldStore(FilePath).Load()));
    }

    [Fact]
    public void Damaged_file_is_kept_aside_and_does_not_crash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(new HoldStore(FilePath).Load());
        Assert.True(File.Exists(FilePath + ".bad"));
        Assert.False(File.Exists(FilePath));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter HoldStoreTests`
Expected: build FAILS with `The type or namespace name 'HoldStore' could not be found`.

- [ ] **Step 3: Implement**

`src/PatchLight.Core/HoldPolicy.cs`:

```csharp
namespace PatchLight.Core;

// Pausing security updates is risky, so holds always end.
public static class HoldPolicy
{
    public const int DefaultDays = 14;
    public const int MaxDays = 35;
}
```

`src/PatchLight.Core/HoldStore.cs`:

```csharp
using System.Text.Json;

namespace PatchLight.Core;

public sealed class HoldStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public IReadOnlyList<Hold> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Hold>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (JsonException)
        {
            // Keep the damaged file for inspection and start fresh.
            File.Move(path, path + ".bad", overwrite: true);
            return [];
        }
    }

    public void Save(IReadOnlyList<Hold> holds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(holds, Json));
        File.Move(temp, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter HoldStoreTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: store paused updates in holds.json"
```

---

### Task 5: Download Microsoft's list, with an offline copy

**Files:**
- Create: `src/PatchLight.Core/IssueSource.cs`
- Test: `tests/PatchLight.Tests/ReleaseHealthSourceTests.cs`

**Interfaces:**
- Consumes: `ReleaseHealthParser.Parse`, `IssueSnapshot`, `PatchLightException`, `ReleaseHealthFormatException` (Task 2).
- Produces: `interface IIssueSource { Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct); }`; `sealed class ReleaseHealthSource(HttpClient http, string cacheDir, TimeProvider time) : IIssueSource`; `static string ReleaseHealthSource.PageUrl(string windowsVersion)`.

Behavior: download → parse → only then save the raw HTML to `<cacheDir>/release-health-<version>.html` (stamp the file time with `time`). On network error, timeout, or unreadable page: parse the saved copy and return `FromCache: true` with `FetchedAt` = file time. No saved copy → `PatchLightException`. User cancellation (Ctrl+C) is not swallowed.

- [ ] **Step 1: Write the failing test**

`tests/PatchLight.Tests/ReleaseHealthSourceTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class ReleaseHealthSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ReleaseHealthSource Source(HttpMessageHandler handler) => new(new HttpClient(handler), _dir, _time);

    [Fact]
    public void Builds_the_page_address_from_the_version() =>
        Assert.Equal(ReleaseHealthParserTests.Url, ReleaseHealthSource.PageUrl("25H2"));

    [Fact]
    public async Task Downloads_and_parses_the_page()
    {
        var handler = new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()));

        var snapshot = await Source(handler).GetAsync("25H2", CancellationToken.None);

        Assert.Equal(11, snapshot.Issues.Count);
        Assert.False(snapshot.FromCache);
        Assert.Equal(_time.GetUtcNow(), snapshot.FetchedAt);
        Assert.Equal(ReleaseHealthParserTests.Url, handler.LastUri?.ToString());
    }

    [Fact]
    public async Task Uses_the_saved_copy_when_offline()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);
        var savedAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromDays(2));

        var snapshot = await Source(new StubHandler(_ => throw new HttpRequestException("offline")))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
        Assert.Equal(savedAt, snapshot.FetchedAt);
    }

    [Fact]
    public async Task Changed_page_does_not_overwrite_the_saved_copy()
    {
        await Source(new StubHandler(_ => Ok(ReleaseHealthParserTests.FixtureHtml()))).GetAsync("25H2", CancellationToken.None);

        var snapshot = await Source(new StubHandler(_ => Ok("<html><body>moved</body></html>")))
            .GetAsync("25H2", CancellationToken.None);

        Assert.True(snapshot.FromCache);
        Assert.Equal(11, snapshot.Issues.Count);
    }

    [Fact]
    public async Task Offline_with_no_saved_copy_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            Source(new StubHandler(_ => throw new HttpRequestException("offline"))).GetAsync("25H2", CancellationToken.None));
        Assert.Contains("Couldn't reach Microsoft", ex.Message);
    }

    [Fact]
    public async Task Server_error_falls_back_like_offline()
    {
        await Assert.ThrowsAsync<PatchLightException>(() =>
            Source(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                .GetAsync("25H2", CancellationToken.None));
    }

    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet add tests/PatchLight.Tests package Microsoft.Extensions.TimeProvider.Testing
dotnet test --filter ReleaseHealthSourceTests
```
Expected: build FAILS with `The type or namespace name 'ReleaseHealthSource' could not be found`.

- [ ] **Step 3: Implement**

`src/PatchLight.Core/IssueSource.cs`:

```csharp
namespace PatchLight.Core;

public interface IIssueSource
{
    Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct);
}

public sealed class ReleaseHealthSource(HttpClient http, string cacheDir, TimeProvider time) : IIssueSource
{
    public static string PageUrl(string windowsVersion) =>
        $"https://learn.microsoft.com/en-us/windows/release-health/status-windows-11-{windowsVersion.ToLowerInvariant()}";

    public async Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct)
    {
        var url = PageUrl(windowsVersion);
        var cachePath = Path.Combine(cacheDir, $"release-health-{windowsVersion.ToLowerInvariant()}.html");
        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            var issues = ReleaseHealthParser.Parse(html, url);

            var now = time.GetUtcNow();
            Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(cachePath, html, ct);
            File.SetLastWriteTimeUtc(cachePath, now.UtcDateTime);
            return new IssueSnapshot(issues, now, FromCache: false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or ReleaseHealthFormatException)
        {
            if (!File.Exists(cachePath))
                throw new PatchLightException(
                    "Couldn't reach Microsoft's known issues list, and there is no saved copy yet. " +
                    "Check your internet connection and try again.");

            var issues = ReleaseHealthParser.Parse(await File.ReadAllTextAsync(cachePath, ct), url);
            var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(cachePath), TimeSpan.Zero);
            return new IssueSnapshot(issues, savedAt, FromCache: true);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ReleaseHealthSourceTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat: download known issues with an offline copy"
```

---

### Task 6: The service — check, pause, resume, history

**Files:**
- Create: `src/PatchLight.Core/IUpdateClient.cs`, `src/PatchLight.Core/PatchLightService.cs`
- Test: `tests/PatchLight.Tests/Fakes.cs`, `tests/PatchLight.Tests/PatchLightServiceTests.cs`

**Interfaces:**
- Consumes: `VerdictEngine.Judge/Affects` (Task 3), `HoldStore`, `HoldPolicy` (Task 4), `IIssueSource` (Task 5), all records (Task 2), `KbNumber.Normalize` (Task 1).
- Produces:

```csharp
public interface IUpdateClient
{
    Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct);   // includes hidden updates
    Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct);    // throws PatchLightException without admin
    Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct);
}
public sealed class PatchLightService(IUpdateClient updates, IIssueSource issues, HoldStore holds, TimeProvider time, string windowsVersion)
{
    Task<CheckResult> CheckAsync(bool applyAutoRelease, CancellationToken ct);
    Task<Hold> HoldAsync(string kbText, int days, string reason, CancellationToken ct);
    Task<ReleasedHold> ReleaseAsync(string kbText, CancellationToken ct);
    Task<IReadOnlyList<HistoryItem>> HistoryAsync(int days, CancellationToken ct);
}
```

Auto-release rules inside `CheckAsync`, per stored hold: update no longer offered → `Gone` (no unhide call); `now >= ExpiresOn` → `Expired`; verdict Green → `Fixed`; otherwise keep. When `applyAutoRelease` is false (not admin) nothing is changed or saved; releasable holds are still reported in `Released` with `ReleaseApplied: false`.

- [ ] **Step 1: Write the fakes**

`tests/PatchLight.Tests/Fakes.cs`:

```csharp
using PatchLight.Core;

namespace PatchLight.Tests;

public sealed class FakeUpdateClient : IUpdateClient
{
    public List<PendingUpdate> Pending { get; } = [];
    public List<InstalledUpdate> History { get; } = [];
    public List<(string Id, bool Hidden)> HideCalls { get; } = [];
    public PatchLightException? HideFailure { get; set; }

    public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PendingUpdate>>(Pending.ToList());

    public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct)
    {
        if (HideFailure is not null) throw HideFailure;
        HideCalls.Add((updateId, hidden));
        var i = Pending.FindIndex(p => p.UpdateId == updateId);
        if (i >= 0) Pending[i] = Pending[i] with { IsHidden = hidden };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InstalledUpdate>>(History.Take(max).ToList());
}

public sealed class FakeIssueSource(TimeProvider time) : IIssueSource
{
    public List<KnownIssue> Issues { get; } = [];

    public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
        Task.FromResult(new IssueSnapshot(Issues.ToList(), time.GetUtcNow(), FromCache: false));
}
```

- [ ] **Step 2: Write the failing test**

`tests/PatchLight.Tests/PatchLightServiceTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class PatchLightServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "patchlight-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateClient _updates = new();
    private readonly FakeIssueSource _issues;
    private readonly HoldStore _holds;
    private readonly PatchLightService _service;

    public PatchLightServiceTests()
    {
        _issues = new FakeIssueSource(_time);
        _holds = new HoldStore(Path.Combine(_dir, "holds.json"));
        _service = new PatchLightService(_updates, _issues, _holds, _time, "25H2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private async Task<Hold> HoldBadUpdate(int days = 14)
    {
        _updates.Pending.Add(Update());
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));
        return await _service.HoldAsync("5127648", days, "Paused by you", CancellationToken.None);
    }

    [Fact]
    public async Task Check_gives_every_pending_update_a_light()
    {
        _updates.Pending.Add(Update());
        _updates.Pending.Add(Update("KB2267602", build: null));
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(new[] { Light.Red, Light.Green }, result.Verdicts.Select(v => v.Light));
        Assert.Equal(_time.GetUtcNow(), result.CheckedAt);
    }

    [Fact]
    public async Task Hold_hides_the_update_for_14_days()
    {
        var hold = await HoldBadUpdate();

        Assert.Equal(("id-KB5127648", true), Assert.Single(_updates.HideCalls));
        Assert.Equal("KB5127648", hold.Kb);
        Assert.Equal(_time.GetUtcNow().AddDays(14), hold.ExpiresOn);
        Assert.Equal(hold, Assert.Single(_holds.Load()));
    }

    [Fact]
    public async Task Hold_never_lasts_more_than_35_days()
    {
        var hold = await HoldBadUpdate(days: 400);
        Assert.Equal(_time.GetUtcNow().AddDays(HoldPolicy.MaxDays), hold.ExpiresOn);
    }

    [Fact]
    public async Task Hold_rejects_text_that_is_not_a_kb()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.HoldAsync("hello", 14, "Paused by you", CancellationToken.None));
        Assert.Contains("KB5127648", ex.Message);
    }

    [Fact]
    public async Task Hold_of_an_update_that_is_not_waiting_fails_and_stores_nothing()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.HoldAsync("KB5000000", 14, "Paused by you", CancellationToken.None));
        Assert.Contains("KB5000000", ex.Message);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Hold_that_could_not_hide_stores_nothing()
    {
        _updates.HideFailure = new PatchLightException("Run PatchLight as administrator to pause or resume updates.");

        await Assert.ThrowsAsync<PatchLightException>(() => HoldBadUpdate());

        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_keeps_the_hold_while_the_problem_is_open()
    {
        await HoldBadUpdate();

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Single(result.ActiveHolds);
        Assert.Empty(result.Released);
        Assert.Single(_updates.HideCalls);
    }

    [Fact]
    public async Task Check_resumes_the_update_once_microsoft_fixes_it()
    {
        await HoldBadUpdate();
        _issues.Issues[0] = _issues.Issues[0] with { Status = IssueStatus.Resolved };

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Fixed, Assert.Single(result.Released).Reason);
        Assert.True(result.ReleaseApplied);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_resumes_the_update_when_the_pause_ends()
    {
        await HoldBadUpdate(days: 7);
        _time.Advance(TimeSpan.FromDays(7));

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Expired, Assert.Single(result.Released).Reason);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
    }

    [Fact]
    public async Task Check_quietly_drops_a_hold_when_windows_replaced_the_update()
    {
        await HoldBadUpdate();
        _updates.Pending.Clear();

        var result = await _service.CheckAsync(applyAutoRelease: true, CancellationToken.None);

        Assert.Equal(ReleaseReason.Gone, Assert.Single(result.Released).Reason);
        Assert.Single(_updates.HideCalls);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Check_without_admin_reports_but_changes_nothing()
    {
        await HoldBadUpdate();
        _issues.Issues.Clear();

        var result = await _service.CheckAsync(applyAutoRelease: false, CancellationToken.None);

        Assert.False(result.ReleaseApplied);
        Assert.Single(result.Released);
        Assert.Single(_updates.HideCalls);
        Assert.Single(_holds.Load());
    }

    [Fact]
    public async Task Release_unhides_and_forgets_the_hold()
    {
        await HoldBadUpdate();

        var released = await _service.ReleaseAsync("kb5127648", CancellationToken.None);

        Assert.Equal(ReleaseReason.Manual, released.Reason);
        Assert.Equal(("id-KB5127648", false), _updates.HideCalls[^1]);
        Assert.Empty(_holds.Load());
    }

    [Fact]
    public async Task Release_of_something_not_paused_is_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<PatchLightException>(() =>
            _service.ReleaseAsync("KB5127648", CancellationToken.None));
        Assert.Contains("isn't paused", ex.Message);
    }

    [Fact]
    public async Task History_points_at_the_update_that_started_an_open_problem()
    {
        var day = _time.GetUtcNow();
        _updates.History.Add(new InstalledUpdate("KB5124008", "2026-09 Security Update (KB5124008) (26200.9445)",
            new Version(26200, 9445), day.AddDays(-30)));
        _updates.History.Add(new InstalledUpdate("KB5124010", "2026-09 Preview Update (KB5124010) (26200.9550)",
            new Version(26200, 9550), day.AddDays(-20)));
        _updates.History.Add(new InstalledUpdate(null, "9NRZT3Q9R3DL-Microsoft.WindowsAppRuntime.2", null, day.AddDays(-1)));
        _issues.Issues.Add(Issue(IssueStatus.Mitigated));
        _issues.Issues.Add(Issue(IssueStatus.Resolved, kb: "KB5124008", build: "26200.9445", title: "Old fixed bug"));

        var history = await _service.HistoryAsync(days: 60, CancellationToken.None);

        Assert.Equal(new[] { "KB5124010", "KB5124008" }, history.Select(h => h.Update.Kb));
        Assert.Single(history[0].Issues);
        Assert.Empty(history[1].Issues);
    }

    [Fact]
    public async Task History_blames_the_first_later_build_when_the_original_is_not_installed()
    {
        var day = _time.GetUtcNow();
        _updates.History.Add(new InstalledUpdate("KB5127648", "2026-10 Security Update (KB5127648) (26200.9711)",
            new Version(26200, 9711), day.AddDays(-1)));
        _issues.Issues.Add(Issue(IssueStatus.Confirmed));

        var history = await _service.HistoryAsync(days: 60, CancellationToken.None);

        Assert.Single(Assert.Single(history).Issues);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter PatchLightServiceTests`
Expected: build FAILS with `The type or namespace name 'IUpdateClient' could not be found`.

- [ ] **Step 4: Implement**

`src/PatchLight.Core/IUpdateClient.cs`:

```csharp
namespace PatchLight.Core;

public interface IUpdateClient
{
    // Includes updates PatchLight (or the user) has hidden.
    Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct);

    // Needs administrator rights; throws PatchLightException with a plain message otherwise.
    Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct);

    Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct);
}
```

`src/PatchLight.Core/PatchLightService.cs`:

```csharp
namespace PatchLight.Core;

public sealed class PatchLightService(
    IUpdateClient updates,
    IIssueSource issues,
    HoldStore holds,
    TimeProvider time,
    string windowsVersion)
{
    public async Task<CheckResult> CheckAsync(bool applyAutoRelease, CancellationToken ct)
    {
        var data = await issues.GetAsync(windowsVersion, ct);
        var pending = await updates.GetPendingAsync(ct);
        var now = time.GetUtcNow();
        var verdicts = pending.Select(u => VerdictEngine.Judge(u, data.Issues, Today())).ToList();

        var active = new List<Hold>();
        var released = new List<ReleasedHold>();
        foreach (var hold in holds.Load())
        {
            var verdict = verdicts.FirstOrDefault(v => v.Update.UpdateId == hold.UpdateId);
            ReleaseReason? reason =
                verdict is null ? ReleaseReason.Gone
                : now >= hold.ExpiresOn ? ReleaseReason.Expired
                : verdict.Light == Light.Green ? ReleaseReason.Fixed
                : null;

            if (reason is null)
            {
                active.Add(hold);
                continue;
            }
            if (applyAutoRelease && verdict is not null)
                await updates.SetHiddenAsync(hold.UpdateId, false, ct);
            released.Add(new ReleasedHold(hold, reason.Value));
        }

        if (applyAutoRelease && released.Count > 0)
            holds.Save(active);

        return new CheckResult(verdicts, active, released, applyAutoRelease, data, now);
    }

    public async Task<Hold> HoldAsync(string kbText, int days, string reason, CancellationToken ct)
    {
        var kb = RequireKb(kbText);
        var update = (await updates.GetPendingAsync(ct)).FirstOrDefault(u => u.Kb == kb)
            ?? throw new PatchLightException($"{kb} isn't waiting to be installed on this PC, so there is nothing to pause.");

        await updates.SetHiddenAsync(update.UpdateId, true, ct);

        var now = time.GetUtcNow();
        var hold = new Hold(kb, update.UpdateId, now, now.AddDays(Math.Clamp(days, 1, HoldPolicy.MaxDays)), reason);
        holds.Save([.. holds.Load().Where(h => h.Kb != kb), hold]);
        return hold;
    }

    public async Task<ReleasedHold> ReleaseAsync(string kbText, CancellationToken ct)
    {
        var kb = RequireKb(kbText);
        var all = holds.Load();
        var hold = all.FirstOrDefault(h => h.Kb == kb)
            ?? throw new PatchLightException($"{kb} isn't paused by PatchLight.");

        if ((await updates.GetPendingAsync(ct)).Any(u => u.UpdateId == hold.UpdateId))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);

        holds.Save([.. all.Where(h => h.Kb != kb)]);
        return new ReleasedHold(hold, ReleaseReason.Manual);
    }

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(int days, CancellationToken ct)
    {
        var data = await issues.GetAsync(windowsVersion, ct);
        var cutoff = time.GetUtcNow().AddDays(-days);
        var installed = (await updates.GetHistoryAsync(500, ct))
            .Where(u => u.Kb is not null && u.InstalledOn >= cutoff)
            .GroupBy(u => u.Kb)
            .Select(g => g.MaxBy(u => u.InstalledOn)!)
            .OrderBy(u => u.InstalledOn)
            .ToList();

        // Blame the update that brought each open problem: the exact KB if installed,
        // otherwise the first later update on the same build line.
        var blame = new Dictionary<string, List<KnownIssue>>();
        foreach (var issue in data.Issues.Where(i => i.Status != IssueStatus.Resolved))
        {
            var culprit = installed.FirstOrDefault(u => u.Kb == issue.OriginatingKb)
                ?? installed.FirstOrDefault(u => VerdictEngine.Affects(issue, kb: null, u.Build));
            if (culprit is null) continue;
            if (!blame.TryGetValue(culprit.Kb!, out var list)) blame[culprit.Kb!] = list = [];
            list.Add(issue);
        }

        return installed
            .OrderByDescending(u => u.InstalledOn)
            .Select(u => new HistoryItem(u, blame.GetValueOrDefault(u.Kb!) ?? []))
            .ToList();
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private static string RequireKb(string text) =>
        KbNumber.Normalize(text)
        ?? throw new PatchLightException($"'{text}' isn't an update number. Use one like KB5127648.");
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS, all 63 tests.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: check, pause, resume and history service"
```

---

### Task 7: Windows layer — Windows Update and system info

**Files:**
- Create: `src/PatchLight.Windows/PatchLight.Windows.csproj`, `src/PatchLight.Windows/WindowsUpdateClient.cs`, `src/PatchLight.Windows/SystemInfo.cs`
- Test: `tests/PatchLight.Tests/WindowsIntegrationTests.cs` (runs against this real PC; read-only)

**Interfaces:**
- Consumes: `IUpdateClient`, `PendingUpdate`, `InstalledUpdate`, `PatchLightException`, `KbNumber`, `UpdateTitle` (Tasks 1–6).
- Produces: `sealed class WindowsUpdateClient : IUpdateClient` (no-arg constructor); `sealed record WindowsInfo(string? DisplayVersion, Version Build, bool IsWindows11)`; `static WindowsInfo SystemInfo.Read()`; `static bool SystemInfo.IsAdministrator()`; `static string SystemInfo.DataDirectory` (= `%ProgramData%\PatchLight`).

Uses late-bound COM (`dynamic` + `Microsoft.Update.Session` ProgID) so no interop assembly is needed. Hiding without admin fails with HRESULT `0x80240044` or `0x80070005` → turn into `PatchLightException("Run PatchLight as administrator to pause or resume updates.")`.

- [ ] **Step 1: Create the project**

```powershell
dotnet new classlib -n PatchLight.Windows -o src/PatchLight.Windows -f net10.0
Remove-Item src/PatchLight.Windows/Class1.cs
dotnet sln add src/PatchLight.Windows
dotnet add src/PatchLight.Windows reference src/PatchLight.Core
dotnet add tests/PatchLight.Tests reference src/PatchLight.Windows
```

In `src/PatchLight.Windows/PatchLight.Windows.csproj` change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>` (the template doesn't accept `-windows` in `-f`).

- [ ] **Step 2: Write the failing test**

`tests/PatchLight.Tests/WindowsIntegrationTests.cs`:

```csharp
using PatchLight.Windows;

namespace PatchLight.Tests;

// Talks to the real Windows Update on this PC. Read-only. Skip with: dotnet test --filter Category!=Integration
[Trait("Category", "Integration")]
public class WindowsIntegrationTests
{
    [Fact]
    public void Detects_windows_11_even_though_the_registry_says_windows_10()
    {
        var info = SystemInfo.Read();
        Assert.True(info.IsWindows11);
        Assert.Matches(@"^\d\dH\d$", info.DisplayVersion!);
        Assert.True(info.Build.Major >= 22000);
    }

    [Fact]
    public void Data_lives_in_program_data() =>
        Assert.EndsWith(@"ProgramData\PatchLight", SystemInfo.DataDirectory, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Lists_pending_updates_with_kb_and_build()
    {
        var pending = await new WindowsUpdateClient().GetPendingAsync(CancellationToken.None);
        Assert.All(pending, u => Assert.False(string.IsNullOrWhiteSpace(u.UpdateId)));
        Assert.All(pending.Where(u => u.Kb is not null), u => Assert.StartsWith("KB", u.Kb));
    }

    [Fact]
    public async Task Reads_install_history()
    {
        var history = await new WindowsUpdateClient().GetHistoryAsync(50, CancellationToken.None);
        Assert.NotEmpty(history);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter Category=Integration`
Expected: build FAILS with `The type or namespace name 'SystemInfo' could not be found`.

- [ ] **Step 4: Implement**

`src/PatchLight.Windows/SystemInfo.cs`:

```csharp
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using PatchLight.Core;

namespace PatchLight.Windows;

public sealed record WindowsInfo(string? DisplayVersion, Version Build, bool IsWindows11);

[SupportedOSPlatform("windows")]
public static class SystemInfo
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PatchLight");

    // ProductName says "Windows 10" on Windows 11, so go by the build number.
    public static WindowsInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new PatchLightException("Couldn't read the Windows version on this PC.");
        var build = int.Parse((string)key.GetValue("CurrentBuild", "0")!);
        var ubr = Convert.ToInt32(key.GetValue("UBR", 0));
        return new WindowsInfo(key.GetValue("DisplayVersion") as string, new Version(build, ubr), build >= 22000);
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

`src/PatchLight.Windows/WindowsUpdateClient.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// Late-bound Windows Update Agent API (Microsoft.Update.Session).
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateClient : IUpdateClient
{
    private const string PendingCriteria = "IsInstalled=0 and IsHidden=0 or IsInstalled=0 and IsHidden=1";
    private const int OperationInstall = 1;
    private const int ResultSucceeded = 2;
    private const int ResultSucceededWithErrors = 3;

    public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) => Task.Run(() =>
    {
        dynamic updates = NewSearcher().Search(PendingCriteria).Updates;
        var list = new List<PendingUpdate>();
        for (var i = 0; i < (int)updates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            list.Add(ToPending(updates[i]));
        }
        return (IReadOnlyList<PendingUpdate>)list;
    }, ct);

    public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct) => Task.Run(() =>
    {
        dynamic updates = NewSearcher().Search(PendingCriteria).Updates;
        for (var i = 0; i < (int)updates.Count; i++)
        {
            dynamic update = updates[i];
            if ((string)update.Identity.UpdateID != updateId) continue;
            try
            {
                update.IsHidden = hidden;
            }
            catch (COMException ex) when ((uint)ex.HResult is 0x80240044 or 0x80070005)
            {
                throw new PatchLightException("Run PatchLight as administrator to pause or resume updates.");
            }
            return;
        }
        throw new PatchLightException("That update is no longer offered by Windows Update.");
    }, ct);

    public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) => Task.Run(() =>
    {
        dynamic searcher = NewSearcher();
        int total = searcher.GetTotalHistoryCount();
        var list = new List<InstalledUpdate>();
        if (total == 0) return (IReadOnlyList<InstalledUpdate>)list;

        dynamic entries = searcher.QueryHistory(0, Math.Min(total, max));
        for (var i = 0; i < (int)entries.Count; i++)
        {
            dynamic entry = entries[i];
            int operation = entry.Operation;
            int result = entry.ResultCode;
            if (operation != OperationInstall || (result != ResultSucceeded && result != ResultSucceededWithErrors)) continue;

            string title = entry.Title;
            DateTime date = entry.Date;
            list.Add(new InstalledUpdate(UpdateTitle.TryGetKb(title), title, UpdateTitle.TryGetBuild(title),
                new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))));
        }
        return (IReadOnlyList<InstalledUpdate>)list;
    }, ct);

    private static PendingUpdate ToPending(dynamic update)
    {
        string title = update.Title;
        string? kb = null;
        dynamic kbs = update.KBArticleIDs;
        if ((int)kbs.Count > 0) kb = KbNumber.Normalize((string)kbs[0]);
        DateTime deployed = update.LastDeploymentChangeTime;
        return new PendingUpdate(
            (string)update.Identity.UpdateID,
            kb ?? UpdateTitle.TryGetKb(title),
            title,
            UpdateTitle.TryGetBuild(title),
            deployed.Year > 2000 ? DateOnly.FromDateTime(deployed) : null,
            (bool)update.IsHidden);
    }

    private static dynamic NewSearcher()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new PatchLightException("Windows Update isn't available on this PC.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "PatchLight";
        return session.CreateUpdateSearcher();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter Category=Integration`
Expected: PASS, 4 tests (the update search can take up to a minute). If `updates[i]` fails with a `RuntimeBinderException`, replace each `collection[i]` with `collection.Item(i)` and rerun.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: Windows Update and system info for Windows 11"
```

---

### Task 8: The `patchlight` command

**Files:**
- Create: `src/PatchLight.Cli/PatchLight.Cli.csproj`, `src/PatchLight.Cli/Program.cs`, `src/PatchLight.Cli/CliApp.cs`, `src/PatchLight.Cli/CliArgs.cs`, `src/PatchLight.Cli/Render.cs`
- Test: `tests/PatchLight.Tests/CliArgsTests.cs`, `tests/PatchLight.Tests/RenderTests.cs`

**Interfaces:**
- Consumes: `PatchLightService`, `ReleaseHealthSource`, `HoldStore`, `HoldPolicy`, records (Core); `WindowsUpdateClient`, `SystemInfo` (Task 7).
- Produces: exe `patchlight.exe` with commands `check` (default), `hold <KB> [--days N]`, `release <KB>`, `history [--days N]`, `help`. Exit codes: 0 fine, 1 a red update is waiting, 2 error, 130 cancelled. `static class CliArgs { int Days(string[] args, int fallback); string Required(string[] args, int index, string example); }`; `static class Render { string Badge(Light); void Check(IAnsiConsole, CheckResult, bool isAdmin); void Held(IAnsiConsole, Hold); void Released(IAnsiConsole, ReleasedHold); void History(IAnsiConsole, IReadOnlyList<HistoryItem>); void Help(IAnsiConsole); }`.

- [ ] **Step 1: Create the project**

```powershell
dotnet new console -n PatchLight.Cli -o src/PatchLight.Cli -f net10.0
dotnet sln add src/PatchLight.Cli
dotnet add src/PatchLight.Cli reference src/PatchLight.Core src/PatchLight.Windows
dotnet add src/PatchLight.Cli package Spectre.Console
dotnet add tests/PatchLight.Tests reference src/PatchLight.Cli
dotnet add tests/PatchLight.Tests package Spectre.Console.Testing
```

In `src/PatchLight.Cli/PatchLight.Cli.csproj` change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>` and add inside the first `<PropertyGroup>`:

```xml
    <AssemblyName>patchlight</AssemblyName>
    <RootNamespace>PatchLight.Cli</RootNamespace>
```

- [ ] **Step 2: Write the failing tests**

`tests/PatchLight.Tests/CliArgsTests.cs`:

```csharp
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
}
```

`tests/PatchLight.Tests/RenderTests.cs`:

```csharp
using PatchLight.Cli;
using PatchLight.Core;
using Spectre.Console.Testing;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public class RenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static CheckResult Result(Light light, bool fromCache = false, IReadOnlyList<ReleasedHold>? released = null,
        bool applied = true)
    {
        var issue = Issue(IssueStatus.Confirmed, title: "Black screen after sign-in");
        var verdict = new UpdateVerdict(Update(), light, "Known problem: Black screen after sign-in",
            light == Light.Green ? [] : [issue], []);
        var data = new IssueSnapshot([issue], fromCache ? Now.AddDays(-2) : Now, fromCache);
        return new CheckResult([verdict], [], released ?? [], applied, data, Now);
    }

    private static string Output(Action<TestConsole> render)
    {
        var console = new TestConsole().Width(200);
        render(console);
        return console.Output;
    }

    [Fact]
    public void Red_update_shows_the_problem_and_how_to_pause_it()
    {
        var text = Output(c => Render.Check(c, Result(Light.Red), isAdmin: false));
        Assert.Contains("Problems", text);
        Assert.Contains("2026-10 Security Update (KB5127648)", text);
        Assert.Contains("Black screen after sign-in", text);
        Assert.Contains("patchlight hold KB5127648", text);
        Assert.Contains("administrator", text);
    }

    [Fact]
    public void Green_update_says_safe() =>
        Assert.Contains("Safe", Output(c => Render.Check(c, Result(Light.Green), isAdmin: true)));

    [Fact]
    public void Says_when_using_the_saved_copy() =>
        Assert.Contains("Couldn't reach Microsoft. Using the copy saved 2 days ago.",
            Output(c => Render.Check(c, Result(Light.Green, fromCache: true), isAdmin: true)));

    [Fact]
    public void Explains_a_resumed_update()
    {
        var hold = new ReleasedHold(new Hold("KB5127648", "id", Now, Now.AddDays(14), "Paused by you"), ReleaseReason.Fixed);
        var text = Output(c => Render.Check(c, Result(Light.Green, released: [hold]), isAdmin: true));
        Assert.Contains("Resumed KB5127648", text);
        Assert.Contains("Microsoft fixed its known problems", text);
    }

    [Fact]
    public void Asks_for_admin_when_a_resume_is_waiting()
    {
        var hold = new ReleasedHold(new Hold("KB5127648", "id", Now, Now.AddDays(14), "Paused by you"), ReleaseReason.Fixed);
        var text = Output(c => Render.Check(c, Result(Light.Green, released: [hold], applied: false), isAdmin: false));
        Assert.Contains("ready to resume", text);
    }

    [Fact]
    public void History_marks_the_likely_cause()
    {
        var item = new HistoryItem(new InstalledUpdate("KB5124010", "2026-09 Preview Update (KB5124010) (26200.9550)",
            new Version(26200, 9550), Now.AddDays(-20)), [Issue(IssueStatus.Confirmed, title: "Remote Desktop fails")]);
        var text = Output(c => Render.History(c, [item]));
        Assert.Contains("Likely cause", text);
        Assert.Contains("Remote Desktop fails", text);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CliArgsTests|FullyQualifiedName~RenderTests"`
Expected: build FAILS with `The type or namespace name 'Cli' does not exist in the namespace 'PatchLight'`.

- [ ] **Step 4: Implement**

`src/PatchLight.Cli/CliArgs.cs`:

```csharp
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
```

`src/PatchLight.Cli/Render.cs`:

```csharp
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

        foreach (var v in result.Verdicts.Where(v => v.Fixes.Count > 0))
            console.MarkupLineInterpolated($"[green]{v.Update.Kb} fixes {v.Fixes.Count} known problem(s).[/]");

        var toPause = result.Verdicts.Where(v => v.Light == Light.Red && !v.Update.IsHidden && v.Update.Kb is not null).ToList();
        if (toPause.Count > 0)
        {
            console.WriteLine();
            foreach (var v in toPause)
                console.MarkupLineInterpolated($"Tip: pause it with [bold]patchlight hold {v.Update.Kb}[/]");
            if (!isAdmin) console.MarkupLine("[grey](Pausing needs a terminal opened as administrator.)[/]");
        }

        foreach (var hold in result.ActiveHolds)
            console.MarkupLineInterpolated($"Paused: {hold.Kb} until {hold.ExpiresOn.ToLocalTime():MMM d}.");

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
            console.MarkupLineInterpolated($"[yellow]Couldn't reach Microsoft. Using the copy saved {Ago(result.CheckedAt - result.Data.FetchedAt)}.[/]");
        else
            console.MarkupLine("[grey]Checked against Microsoft's known issues just now.[/]");
    }

    public static void Held(IAnsiConsole console, Hold hold) =>
        console.MarkupLineInterpolated(
            $"[green]Paused {hold.Kb}[/] until {hold.ExpiresOn.ToLocalTime():MMM d}. PatchLight resumes it as soon as Microsoft fixes it.");

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
        console.WriteLine();
        console.MarkupLine("[grey]Pausing and resuming need a terminal opened as administrator.[/]");
    }

    private static string Why(ReleaseReason reason) => reason switch
    {
        ReleaseReason.Fixed => "Microsoft fixed its known problems",
        ReleaseReason.Expired => "the pause time ended",
        ReleaseReason.Gone => "Windows replaced it with a newer update",
        _ => "resumed by you",
    };

    private static string Ago(TimeSpan age) =>
        age.TotalHours < 1 ? "less than an hour ago"
        : age.TotalHours < 48 ? $"{(int)age.TotalHours} hours ago"
        : $"{(int)age.TotalDays} days ago";
}
```

`src/PatchLight.Cli/CliApp.cs`:

```csharp
using PatchLight.Core;
using PatchLight.Windows;
using Spectre.Console;

namespace PatchLight.Cli;

public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var console = AnsiConsole.Console;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var command = args.Length == 0 ? "check" : args[0].ToLowerInvariant();
            if (command is "help" or "--help" or "-h" or "/?")
            {
                Render.Help(console);
                return 0;
            }

            var windows = SystemInfo.Read();
            if (!windows.IsWindows11 || windows.DisplayVersion is null)
                throw new PatchLightException("PatchLight works on Windows 11 only for now.");

            var service = CreateService(windows.DisplayVersion);
            var isAdmin = SystemInfo.IsAdministrator();

            switch (command)
            {
                case "check":
                    var result = await console.Status().StartAsync(
                        "Checking Windows Update and Microsoft's known issues...",
                        _ => service.CheckAsync(applyAutoRelease: isAdmin, cts.Token));
                    Render.Check(console, result, isAdmin);
                    return result.Verdicts.Any(v => v.Light == Light.Red && !v.Update.IsHidden) ? 1 : 0;

                case "hold":
                    var kbToHold = CliArgs.Required(args, 1, "hold KB5127648");
                    var days = CliArgs.Days(args, HoldPolicy.DefaultDays);
                    RequireAdmin(isAdmin);
                    var hold = await console.Status().StartAsync("Pausing...",
                        _ => service.HoldAsync(kbToHold, days, "Paused by you", cts.Token));
                    Render.Held(console, hold);
                    return 0;

                case "release":
                    var kbToRelease = CliArgs.Required(args, 1, "release KB5127648");
                    RequireAdmin(isAdmin);
                    var released = await console.Status().StartAsync("Resuming...",
                        _ => service.ReleaseAsync(kbToRelease, cts.Token));
                    Render.Released(console, released);
                    return 0;

                case "history":
                    var history = await console.Status().StartAsync("Reading update history...",
                        _ => service.HistoryAsync(CliArgs.Days(args, 60), cts.Token));
                    Render.History(console, history);
                    return 0;

                default:
                    console.MarkupLineInterpolated($"[red]Unknown command '{command}'.[/]");
                    Render.Help(console);
                    return 2;
            }
        }
        catch (PatchLightException ex)
        {
            console.MarkupLineInterpolated($"[red]{ex.Message}[/]");
            return 2;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    private static PatchLightService CreateService(string windowsVersion)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PatchLight/0.1 (+https://github.com/)");
        var source = new ReleaseHealthSource(http, Path.Combine(SystemInfo.DataDirectory, "cache"), TimeProvider.System);
        var holds = new HoldStore(Path.Combine(SystemInfo.DataDirectory, "holds.json"));
        return new PatchLightService(new WindowsUpdateClient(), source, holds, TimeProvider.System, windowsVersion);
    }

    private static void RequireAdmin(bool isAdmin)
    {
        if (!isAdmin)
            throw new PatchLightException(
                "Pausing and resuming need administrator rights. Open Terminal as administrator and run this again.");
    }
}
```

`src/PatchLight.Cli/Program.cs` (replace the template content):

```csharp
using PatchLight.Cli;

return await CliApp.RunAsync(args);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: PASS, all tests (unit + integration).

- [ ] **Step 6: Try it on this PC (manual)**

```powershell
dotnet run --project src/PatchLight.Cli
dotnet run --project src/PatchLight.Cli -- history
dotnet run --project src/PatchLight.Cli -- hold KB5127648
```
Expected:
1. A table with the 9 pending updates. On 2026-10-13 `KB5127648` shows `● Wait` (Remote Desktop + USB audio issues are Mitigated) and "KB5127648 fixes 2 known problem(s)".
2. A history table.
3. Without admin: "Pausing and resuming need administrator rights..." exit code 2.

Then in a terminal opened **as administrator**: `hold KB5127648`, confirm in Settings → Windows Update that it no longer shows, then `release KB5127648` and confirm it is back.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat: patchlight command with check, hold, release and history"
```

---

### Task 9: README, license, single-file build

**Files:**
- Create: `README.md`, `LICENSE`

**Interfaces:**
- Consumes: the CLI from Task 8.
- Produces: `dist/patchlight.exe` (self-contained, single file) and a README that explains the project to a stranger.

- [ ] **Step 1: Publish and verify the exe runs**

```powershell
dotnet publish src/PatchLight.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
./dist/patchlight.exe help
```
Expected: the help text. Add `dist/` to `.gitignore`.

- [ ] **Step 2: Write `LICENSE`** — standard MIT text, `Copyright (c) 2026 G4SBO`.

- [ ] **Step 3: Write `README.md`**

```markdown
# PatchLight 🟢🟡🔴

**Know if a Windows update is safe before you install it.**

Windows updates break PCs more often than they should: black screens, Remote Desktop
failures, broken audio. PatchLight checks every update waiting on your PC against
Microsoft's own list of known problems and gives it a light:

| Light | Meaning |
|---|---|
| 🟢 Safe | No known problems. |
| 🟡 Wait | A problem with a workaround, or it came out less than 3 days ago. |
| 🔴 Problems | Microsoft confirmed it breaks something. Pause it. |

Pause only the bad ones. PatchLight resumes them as soon as Microsoft fixes them,
and never pauses anything for more than 35 days, because security updates matter.

## Use it

```
patchlight                       Check the updates waiting on this PC
patchlight hold KB5127648        Pause an update (needs administrator)
patchlight release KB5127648     Resume it
patchlight history               Which recent update likely broke something
```

Windows 11 only. Free and open source. No account, no server: your PC reads
Microsoft's public known-issues page directly.

## Build

Requires the .NET 10 SDK.

```
dotnet test
dotnet run --project src/PatchLight.Cli
```

## License

MIT
```

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m "docs: README, MIT license, single-file publish"
```
