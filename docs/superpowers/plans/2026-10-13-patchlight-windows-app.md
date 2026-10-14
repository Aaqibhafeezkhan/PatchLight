# PatchLight Windows App Implementation Plan (Plan 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A WinUI 3 desktop app with a live traffic-light tray icon, one-click pause/resume, notifications, and a daily background check that resumes fixed updates — built on the Plan 1 engine.

**Architecture:** All decision logic stays in `PatchLight.Core` (pure, unit-tested): overall light, update cards, alerts, app state. `PatchLight.Windows` gains the OS glue (shared service factory, data-folder lockdown, scheduled task, autostart, elevation protocol, background check). A new `PatchLight.App` (WinUI 3, unpackaged, self-contained Windows App SDK) is a thin UI over both. The app runs as a normal user; anything that changes Windows Update relaunches the same exe elevated with a headless command (one UAC prompt). The daily scheduled task runs the same exe headless as SYSTEM.

**Tech Stack:** .NET 10, WinUI 3 (Microsoft.WindowsAppSDK 2.5.1), H.NotifyIcon.WinUI 2.4.1, xunit.

**Spec:** `docs/superpowers/specs/2026-10-13-patchlight-design.md` (Roadmap item 2).

## Design decisions made for this plan (2026-10-13, LO away — recorded as rulings)

- **Notifications go through the tray icon** (`TaskbarIcon.ShowNotification`). Verified by spike: Windows App SDK `AppNotificationManager.Register()` throws 0x8007007E in a self-contained unpackaged app; the tray route works and still shows as a Windows notification.
- **Custom entry point** (`DISABLE_XAML_GENERATED_MAIN`): headless modes run before any UI starts. Verified by spike.
- **Elevation protocol:** `PatchLight.exe --hold KB --days N --result <file>`, `--release KB --result <file>`, `--enable-daily-check --result <file>`, `--disable-daily-check --result <file>` are started with `Verb=runas`. The child writes an empty file on success or one plain-English line on failure. UAC "No" → "Windows asked for permission and it wasn't given, so nothing changed."
- **Daily check:** scheduled task `\PatchLight\Daily check`, runs as SYSTEM, daily 10:00, start-when-available, needs network, 30 min limit, runs `"<exe>" --background-check`. It applies auto-release and writes `%ProgramData%\PatchLight\last-background-check.json`. Turned on/off from Settings (one UAC prompt).
- **Data folder lockdown:** every elevated entry point (CLI as admin, elevated app commands, background check) first calls `DataFolder.Secure`: inheritance off; SYSTEM + Administrators full control; Users read & execute; existing files' owner set to Administrators (best effort). Closes the Plan 1 deferred item before SYSTEM reads the folder.
- **Per-user state** (`%LOCALAPPDATA%\PatchLight\app-state.json`): pause length (default 14) and which alerts were already shown.
- **Start with Windows:** HKCU Run value `PatchLight` = `"<exe>" --tray` (starts hidden in the tray). On by default the first time the app runs.
- **Single instance:** named mutex `PatchLight.App.Instance`; a second launch signals event `PatchLight.App.Show` and exits.
- **Closing the window hides it to the tray;** "Exit" in the tray menu quits.
- **App checks** at start and every 6 hours while running (read-only, no admin), plus "Check now".
- **Demo mode** `PatchLight.exe --demo`: built-in sample updates (one of each light) and a temp holds file; pausing is simulated with no admin and no real Windows Update. Used for screenshots/GIFs and for safe UI testing.
- **No full-screen screenshots** when verifying UI: capture only the PatchLight window.

## Global Constraints

- English UI, plain everyday words. No jargon in user-facing text.
- Windows 11 only (`CurrentBuild >= 22000`).
- Warnings are errors (Directory.Build.props). `dotnet test` must stay green.
- Never run a real hold/release/hide on this PC from automation. Demo mode only.
- Never modify `tests/PatchLight.Tests/Fixtures/status-windows-11-25h2.html`.
- Pauses always end (max 35 days); security matters.
- Free, no server, no telemetry.

## Review Focus

1. UAC declined or elevated child crashes → plain message, UI stays usable, nothing half-done.
2. No internet / Windows Update service stopped at app start → gray "Couldn't check" state with the reason, tray still works, retry later.
3. Old or damaged `app-state.json` → defaults, no crash.
4. Exe moved after enabling the daily check → Settings shows the daily check as "needs turning on again" (task path ≠ current exe).
5. Background check fails (offline) → error recorded in `last-background-check.json`, exit 0 quietly, no UI.

---

### Task 1: Core — overall light and update cards

**Files:** Create `src/PatchLight.Core/StatusSummary.cs`, `src/PatchLight.Core/UpdateCard.cs`; Test `tests/PatchLight.Tests/StatusSummaryTests.cs`, `tests/PatchLight.Tests/UpdateCardTests.cs`.

**Produces:**
- `static Light StatusSummary.Overall(CheckResult r)` — worst light among verdicts whose update is not hidden; Green if none.
- `static string StatusSummary.Headline(Light)` — Green "All clear", Yellow "Wait a few days", Red "Don't install yet".
- `static string StatusSummary.Detail(CheckResult r)` — no updates: "Nothing is waiting to install. You're up to date."; Green: "No known problems with the updates waiting on this PC."; Yellow: "1 update has a known problem with a workaround or is brand new." / "{n} updates have …"; Red: "1 update has known problems. Pause it until Microsoft fixes it." / "{n} updates have known problems. Pause them until Microsoft fixes them." (count = non-hidden verdicts with that light).
- `static string StatusSummary.CheckedText(CheckResult r)` — not from cache: "Checked against Microsoft's known issues just now."; from cache: "Couldn't reach Microsoft. Using the copy saved {ago}." (same Ago wording as `Render.Ago`; move Ago into StatusSummary as `public static string Ago(TimeSpan)` and make Render use it).
- `sealed record UpdateCard(string UpdateId, string? Kb, string Title, Light Light, string Badge, string Reason, IReadOnlyList<KnownIssue> Issues, int FixCount, string? PausedText, bool CanPause, bool CanResume)`.
- `static IReadOnlyList<UpdateCard> UpdateCard.Build(CheckResult r)` — ordered worst light first, then title. Badge "Safe"/"Wait"/"Problems". Holds = ActiveHolds + Released.Select(x=>x.Hold) matched by UpdateId. PausedText: own hold → "Paused until {ExpiresOn local, MMM d}"; hidden without own hold → "Hidden in Windows Update"; else null. CanPause = Kb not null && !hidden && Light != Green. CanResume = hidden && own hold exists.

**Tests:** overall ignores hidden red; overall green when empty; detail singular/plural for yellow and red; checked text both branches; cards order; card for hidden update with hold shows PausedText & CanResume; hidden without hold → "Hidden in Windows Update", CanResume false; green card CanPause false; Kb null CanPause false; FixCount.

### Task 2: Core — alerts and per-user app state

**Files:** Create `src/PatchLight.Core/AlertPlanner.cs`, `src/PatchLight.Core/AppState.cs`; Tests `AlertPlannerTests.cs`, `AppStateStoreTests.cs`.

**Produces:**
- `sealed record AppState(int PauseDays = 14, IReadOnlyList<string>? NotifiedProblems = null, IReadOnlyList<string>? NotifiedFixed = null)` with non-null init properties (same pattern as `Hold.IssueIds`) and list-aware Equals. PauseDays clamped 1..35 on load.
- `sealed class AppStateStore(string path)` — `AppState Load()` (missing/damaged/null → `new AppState()`), `void Save(AppState)` (temp + move, creates folder).
- `sealed record Alert(string Title, string Body)`.
- `static (IReadOnlyList<Alert> Alerts, AppState Next) AlertPlanner.Plan(AppState state, CheckResult r)`:
  - Problems = non-hidden Red verdicts. New = those whose UpdateId ∉ NotifiedProblems. 1 new → Title "Don't install {kb ?? title} yet", Body = verdict.Reason + " Open PatchLight to pause it."; >1 new → "{n} updates have known problems", Body "Open PatchLight to see them and pause them.". Next.NotifiedProblems = all current problem UpdateIds (so a problem that goes away and returns alerts again).
  - Fixed = Released with Reason Fixed. Key = hold Kb. New ones → one alert each: if ReleaseApplied "Microsoft fixed {kb}" / "PatchLight resumed it. Windows Update will install it."; else "Microsoft fixed {kb}" / "The daily check will resume it, or open PatchLight to resume it now.". Next.NotifiedFixed = current fixed Kbs.

**Tests:** first red alerts once, second plan with returned state no alert; two new reds → grouped; red gone then back → alerts again; hidden red never alerts; fixed alert once, wording by ReleaseApplied; store round-trip, damaged file → defaults, missing fields → defaults, PauseDays 99 → 35.

### Task 3: Windows — shared host, data folder lockdown, background check

**Files:** Create `src/PatchLight.Windows/PatchLightHost.cs`, `DataFolder.cs`, `BackgroundCheck.cs`; Modify `src/PatchLight.Cli/CliApp.cs` (use host; `DataFolder.Secure(SystemInfo.DataDirectory)` when admin, before the service); Tests `DataFolderTests.cs`, `BackgroundCheckTests.cs`.

**Produces:**
- `static PatchLightService PatchLightHost.CreateService(string windowsVersion, string? dataDirectory = null)` — the Plan 1 factory moved here; user agent `PatchLight/0.2 (+https://github.com/yo-gabriel-portfolio-here/PatchLight)`.
- `static void DataFolder.Secure(string path)` — as in Design decisions. Owner change best effort (ignore `InvalidOperationException`, `UnauthorizedAccessException`, `System.Security.AccessControl.PrivilegeNotHeldException`).
- `sealed record BackgroundCheckLog(DateTimeOffset RanAt, IReadOnlyList<string> Resumed, string? Error)`.
- `static class BackgroundCheck` — `Task<BackgroundCheckLog> RunAsync(PatchLightService service, string logPath, TimeProvider time, CancellationToken ct)`: runs `CheckAsync(applyAutoRelease: true)`; Resumed = released Kbs except Gone; any exception → Error = PatchLightException message or "Something went wrong: {type}"; always writes the log JSON (best effort) and returns it. `BackgroundCheckLog? ReadLog(string logPath)` (null if missing/damaged).

**Tests:** Secure on a temp dir: DACL protected, no rule grants Users write/modify, SYSTEM & Administrators full control; a file created before Secure is no longer writable-by-Users by rule. BackgroundCheck with fakes: resumes fixed hold & logs it; issue source throwing PatchLightException → logged error, no throw; ReadLog round trip; damaged log → null.

### Task 4: Windows — daily task, autostart, elevation

**Files:** Create `src/PatchLight.Windows/DailyTask.cs`, `Autostart.cs`, `Elevation.cs`, `ElevatedCommand.cs`; Tests `DailyTaskTests.cs`, `AutostartTests.cs`, `ElevatedCommandTests.cs`.

**Produces:**
- `static class DailyTask` — `const string Name = @"\PatchLight\Daily check"`; `string BuildXml(string exePath)` (Task Scheduler 1.2 XML: SYSTEM S-1-5-18, HighestAvailable, CalendarTrigger daily 10:00, StartWhenAvailable, RunOnlyIfNetworkAvailable, ExecutionTimeLimit PT30M, battery restrictions off, Exec `exePath` args `--background-check`; exePath XML-escaped); `void Register(string exePath)` (writes UTF-16 temp xml, `schtasks /Create /TN Name /XML file /F`, non-zero exit → PatchLightException "Couldn't turn on the daily check."); `void Unregister()` (missing task is fine); `string? RegisteredExePath()` (`schtasks /Query /TN Name /XML`, parse `<Command>`; null when missing).
- `sealed class Autostart(RegistryKey runKey)` + `static Autostart ForCurrentUser()` (HKCU Run) — `bool IsEnabled(string exePath)`, `void Enable(string exePath)` (value `"exe" --tray`), `void Disable()`.
- `sealed record ElevatedCommand(string Verb, string? Kb, int Days, string ResultPath)`; `static ElevatedCommand? Parse(string[] args)` for `--hold KB [--days N] --result P`, `--release KB --result P`, `--enable-daily-check --result P`, `--disable-daily-check --result P` (null otherwise; days default 14, clamp 1..35); `Task<int> RunAsync(Func<PatchLightService> service, string exePath, CancellationToken ct)` — secures data folder, runs the action, writes "" or the message to ResultPath; returns 0/2. Unexpected exceptions → "Something went wrong." + 2. Hooks for tests: `RunAsync` takes the actions via an `IElevatedActions` interface (`HoldAsync`, `ReleaseAsync`, `EnableDailyCheck`, `DisableDailyCheck`) with a real implementation `ElevatedActions(PatchLightService, string exePath)`.
- `static class Elevation` — `Task RunSelfElevatedAsync(IReadOnlyList<string> args, CancellationToken ct)`: temp result path appended as `--result`, `Process.Start` with `UseShellExecute=true, Verb="runas"`, waits; `Win32Exception` 1223 → PatchLightException("Windows asked for permission and it wasn't given, so nothing changed."); non-empty result text → PatchLightException(text); missing file with non-zero exit → "Something went wrong."; deletes the temp file.

**Tests:** XML parses, contains S-1-5-18, `--background-check`, escaped path with `&`; Autostart on a temp HKCU subkey enable/isEnabled/disable/path mismatch; Parse cases incl. invalid; RunAsync with a fake actions object writes "" on success, message on PatchLightException, returns codes.

### Task 5: App project — shell, entry point, tray

**Files:** Create `src/PatchLight.App/PatchLight.App.csproj`, `Program.cs`, `App.xaml(.cs)`, `MainWindow.xaml(.cs)` (placeholder content), `TrayIcon` in App, `Assets/*.ico` (green/yellow/red/gray dot + app icon), `app.manifest` (per-monitor DPI); Modify `PatchLight.slnx`.

- csproj: WinExe, `net10.0-windows10.0.22621.0`, min 10.0.22000.0, UseWinUI, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, RuntimeIdentifier win-x64, Platforms x64, `DefineConstants DISABLE_XAML_GENERATED_MAIN`, AssemblyName `PatchLight`, ApplicationIcon. References Core + Windows.
- Program.Main: `--background-check` → BackgroundCheck with secured data folder, exit 0; elevated commands → ElevatedCommand; otherwise single-instance check then start WinUI (`--tray` = start hidden, `--demo` = demo mode).
- Tray: icon by overall light (gray while checking/failed), tooltip "PatchLight — {Headline}", menu Open / Check now / Exit; double-click opens.

### Task 6: App — main window

**Files:** `src/PatchLight.App/MainWindow.xaml(.cs)`, `AppController.cs` (check loop, state, alerts, actions), `DemoData.cs`, `Views/UpdatesView`, `Views/HistoryView`, `Views/SettingsView`.

- Header: three-lamp traffic light (active lamp lit), Headline, Detail, CheckedText, "Check now".
- Updates: one card per `UpdateCard`: dot + badge, title, reason, known problems (title + status + "Read Microsoft's note" link), "Fixes N known problems", PausedText, Pause / Resume buttons (Pause uses `AppState.PauseDays`).
- Error state: gray lamp, "Couldn't check", message, "Try again".
- History: date, title, "None" or "Likely cause: …".
- Settings: Start with Windows toggle, Daily automatic check toggle (+ last run text from `last-background-check.json`, "needs turning on again" when path differs), pause length (1–35), About (version, GitHub link, "Data comes from Microsoft's public release health pages").
- Notifications from AlertPlanner after each check; state saved.
- Window ~ 900×700, Mica backdrop, title "PatchLight", close → hide to tray.

### Task 7: Docs, publish script, CI

- README: the app first (what it shows, tray, daily check, pause), CLI second, build/publish, how data folder/daily task work, uninstall notes.
- `scripts/publish.ps1`: publishes App (self-contained folder) and CLI (single file) to `dist/`.
- `.github/workflows/ci.yml`: windows-latest, .NET 10, `dotnet build`, `dotnet test --filter Category!=Integration`.
