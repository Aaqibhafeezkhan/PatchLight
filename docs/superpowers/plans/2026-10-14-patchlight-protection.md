# PatchLight Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Act before Windows stages an update: optional auto-pause of red updates, an hourly background check, and an optional "wait N days before new updates" setting.

**Architecture:** Protection settings live in an admin-only `C:\ProgramData\PatchLight\settings.json`, written by the elevated helper and read by the SYSTEM background check. The background check (now hourly) runs the normal check, then `PatchLightService.ProtectAsync` pauses red updates and (on Home) young OS updates. On Pro and up, "wait" is Windows' own `DeferQualityUpdates` policy, written only by the elevated helper and only when no one else owns it.

**Tech Stack:** C# / .NET 10, WinUI 3 app, xUnit tests, Task Scheduler XML, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-10-14-patchlight-protection-design.md`

## Global Constraints

- All UI text, code, comments and docs in English.
- Defaults: auto-pause off, wait Off (0). Allowed wait values: 0, 3, 7, 14.
- Pauses always end (`HoldPolicy.MaxDays = 35`); auto-pause must never re-pause an update whose pause just ran out or that the person resumed, unless it has a new problem id.
- Yellow is never auto-paused. News never triggers auto-pause.
- The scheduled task keeps the name `\PatchLight\Daily check`.
- Never write or delete policy values PatchLight doesn't own.
- Build: `dotnet test` from repo root with `C:\Program Files\dotnet` on PATH.
- No Claude attribution lines in commits.

## Review Focus

- Pause that expired while the update is still red → must NOT be auto-paused again (pin in Task 2).
- Update hidden by someone else (not by a PatchLight hold) → auto-pause leaves it alone (Task 2).
- Feature upgrade (26H2) offered → never gets a Home wait-hold (Task 2).
- Company already set `DeferQualityUpdatesPeriodInDays` → setting refuses, nothing is overwritten or deleted (Task 3).
- `settings.json` missing or damaged → background check behaves exactly like today (Task 1/4).

---

### Task 1: Protection settings + store

**Files:**
- Create: `src/PatchLight.Core/ProtectionSettings.cs`
- Test: `tests/PatchLight.Tests/ProtectionSettingsTests.cs`

**Produces:**
```csharp
public sealed record LetThrough(string Kb, IReadOnlyList<string> IssueIds);
public sealed record ProtectionSettings(bool AutoPauseRed = false, int WaitDays = 0, int PauseDays = HoldPolicy.DefaultDays,
    bool OwnsDeferPolicy = false, IReadOnlyList<LetThrough>? LetThrough = null)
{
    public static readonly int[] WaitChoices = [0, 3, 7, 14];
    // WaitDays snapped to an allowed value, PauseDays clamped, LetThrough never null
}
public sealed class ProtectionStore(string path) { ProtectionSettings Load(); void Save(ProtectionSettings s); }
```
Load returns defaults on missing/damaged file (same pattern as `AppStateStore`).

- [ ] Tests: missing file → defaults; damaged → defaults; round trip equal; WaitDays 5 → 0 (not an allowed choice); PauseDays 400 → 35.
- [ ] Implement, run tests, commit `feat: protection settings file`.

### Task 2: `PatchLightService.ProtectAsync`

**Files:**
- Modify: `src/PatchLight.Core/PatchLightService.cs`
- Test: `tests/PatchLight.Tests/ProtectionTests.cs`

**Consumes:** Task 1 types.
**Produces:**
```csharp
public const string AutoReason = "Paused automatically";
public const string WaitReason = "Waiting after release";
public sealed record ProtectionOutcome(IReadOnlyList<Hold> Paused, IReadOnlyList<LetThrough> LetThrough);
public Task<ProtectionOutcome> ProtectAsync(CheckResult result, ProtectionSettings settings, bool waitWithHolds, CancellationToken ct);
```
Rules (in order):
1. `letThrough` = settings.LetThrough + one entry per `result.Released` with reason `Expired` whose hold reason isn't `WaitReason` (KB + hold.IssueIds). Entries for KBs no longer pending are dropped.
2. An update is *free* when: `PauseKey` not null, no active hold with that key, and either not hidden or its hold was just released in this result.
3. Auto-pause (if `AutoPauseRed`): free + `Light.Red` + not let-through (let-through matches when same KB and every current issue id is in its list) → hold `PauseDays`, reason `AutoReason`, issue ids = verdict issues.
4. Wait (if `waitWithHolds && WaitDays > 0`): free + OS update + not feature update + `Released` known + `today < Released + WaitDays` + not already paused in step 3 → hold until `Released + WaitDays` (UTC midnight), reason `WaitReason`, no issue ids.
5. Holds are created through a shared private `PlaceHoldAsync(PendingUpdate, DateTimeOffset expires, string reason, IReadOnlyList<string> ids)` that `HoldAsync` also uses.

- [ ] Tests: red paused; yellow not; green not; already-held not; hidden by someone else not; resumed-by-hand with same ids not, with a new id yes; expired red pause not re-paused and returned in LetThrough; Home wait hold only for young OS updates (not feature upgrade, not non-OS), expiry = release + N; wait off when `waitWithHolds` false; expired wait-hold that is red gets auto-paused in the same run.
- [ ] Implement, tests pass, commit `feat: auto-pause red updates and wait-holds`.

### Task 3: Defer policy (Pro and up)

**Files:**
- Create: `src/PatchLight.Windows/DeferPolicy.cs`
- Modify: `src/PatchLight.Windows/SystemInfo.cs` (add `IsHomeEdition()`: `EditionID` starts with `Core`)
- Test: `tests/PatchLight.Tests/DeferPolicyTests.cs`

**Produces:**
```csharp
public interface IPolicyValues { int? Get(string name); void Set(string name, int value); void Delete(string name); }
public static class DeferPolicy
{
    public const string Enabled = "DeferQualityUpdates", Period = "DeferQualityUpdatesPeriodInDays";
    public static bool IsManagedElsewhere(IPolicyValues values, bool owns); // values exist && !owns
    public static bool Apply(IPolicyValues values, int days, bool owns);    // returns new "owns"; throws PatchLightException when managed elsewhere
}
public sealed class WindowsUpdatePolicyValues : IPolicyValues // HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate
```
- [ ] Tests (fake dictionary): days 7 writes 1/7, owns true; days 0 with owns deletes both, owns false; days 0 without owns and foreign values → untouched, returns false, no throw; days 7 with foreign values → throws "managed by your organization", untouched.
- [ ] Implement, commit `feat: Windows deferral policy for the wait setting`.

### Task 4: Background check runs protection + hourly task

**Files:**
- Modify: `src/PatchLight.Windows/BackgroundCheck.cs`, `src/PatchLight.Windows/DailyTask.cs`, `src/PatchLight.Windows/Backend.cs` (`DailyCheckState.Outdated`), `src/PatchLight.App/Program.cs`, `src/PatchLight.App/Backends.cs`, `src/PatchLight.Windows/PatchLightHost.cs` (`SettingsPath`)
- Test: `tests/PatchLight.Tests/BackgroundCheckTests.cs`, `tests/PatchLight.Tests/DailyTaskTests.cs`

**Produces:**
- `BackgroundCheckLog(DateTimeOffset RanAt, IReadOnlyList<string> Resumed, string? Error, IReadOnlyList<string>? Paused = null)` (`Paused` never null after init).
- `BackgroundCheck.RunAsync(service, logPath, time, ct, ProtectionStore? protection = null, bool waitWithHolds = false)`; after a successful check, loads settings, runs `ProtectAsync`, saves settings with the new `LetThrough` when it changed, logs `Paused` keys. A protection failure is logged as the error but resumes still count.
- `DailyTask.BuildXml` daily trigger gets `<Repetition><Interval>PT1H</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>`.
- `DailyTask.IsCurrent(string xml)` (has `PT1H`), `DailyTask.RegisteredXml()`, `DailyTask.NeedsRegister(string exePath)` (missing, moved, or not current).
- `--refresh-daily-check` (Program.Main, before other modes): if a task exists, register it again for this exe; never creates one.
- Backends.Real maps registered-but-not-current to `DailyCheckState.Outdated`.

- [ ] Tests: log carries Paused; settings LetThrough saved after an expired red pause; damaged settings → like today; XML has hourly repetition; `IsCurrent` false for old XML.
- [ ] Implement, commit `feat: hourly background check that applies protection`.

### Task 5: Elevated `--set-protection` + resume records let-through

**Files:**
- Modify: `src/PatchLight.Windows/ElevatedCommand.cs`, `src/PatchLight.Windows/Elevation.cs`, `src/PatchLight.App/DemoData.cs`, `src/PatchLight.App/Program.cs`, test fakes in `ElevatedCommandTests.cs` and `AppControllerTests.cs`
- Test: `tests/PatchLight.Tests/ElevatedCommandTests.cs`

**Produces:**
```csharp
public sealed record ProtectionChoice(bool AutoPauseRed, int WaitDays, int PauseDays);
// IElevatedActions: Task<string?> SetProtectionAsync(ProtectionChoice choice, CancellationToken ct);
// ElevatedCommand: Verb "--set-protection", new property ProtectionChoice? Protection
public static IReadOnlyList<string> SetProtectionArgs(ProtectionChoice c) =>
    ["--set-protection", "--auto-pause", c.AutoPauseRed ? "on" : "off", "--wait-days", c.WaitDays.ToString(), "--days", c.PauseDays.ToString()];
```
`ElevatedActions(service, exePath, ProtectionStore? store = null, IPolicyValues? policy = null, bool isHome = false)`:
- `SetProtectionAsync`: on non-Home apply `DeferPolicy` (throws when managed elsewhere, nothing saved), save settings, ensure the task when anything is on (warning if it can't), `WindowsUpdateScan.Start()`.
- `ReleaseAsync`: adds `LetThrough(kb, hold.IssueIds)` to settings.
- `EnsureDailyCheck` registers when `DailyTask.NeedsRegister(exePath)`.
- Uninstall cleanup removes owned policy values.

- [ ] Tests: parse round trip; wait 5 rejected (null); auto-pause "maybe" rejected; RunAsync calls `SetProtectionAsync`.
- [ ] Implement, commit `feat: elevated helper changes protection settings`.

### Task 6: App: settings card, alert, wording

**Files:**
- Modify: `src/PatchLight.Core/AlertPlanner.cs`, `src/PatchLight.Windows/AppController.cs`, `src/PatchLight.Windows/Backend.cs`, `src/PatchLight.App/Backends.cs`, `src/PatchLight.App/MainWindow.xaml.cs`, `src/PatchLight.App/App.xaml.cs`
- Test: `tests/PatchLight.Tests/AlertPlannerTests.cs`, `tests/PatchLight.Tests/AppControllerTests.cs`

**Produces:**
- `AlertPlanner.PlanBackground(state, ranAt, resumed, paused)`: one alert for paused ("PatchLight paused KB… automatically" / "PatchLight paused N updates automatically") and the resumed alert, both possible.
- Backend: `Func<ProtectionSettings> GetProtection`, `bool IsHomeEdition`, `Func<bool> DeferManagedElsewhere`.
- AppController: `ProtectionSettings Protection`, `Task<ActionResult> SetProtectionAsync(bool autoPause, int waitDays)` (passes `State.PauseDays`), `bool IsHomeEdition`, `bool DeferManagedElsewhere`.
- Settings page "Protection" card (toggle + ComboBox Off/3/7/14), wording "Automatic check (every hour)", Outdated state text, pause message "The automatic check resumes it…".
- Demo actions store the choice in memory.

- [ ] Tests: paused alert single/multi; paused + resumed gives two alerts; controller passes pause days to SetProtection.
- [ ] Implement, build the app, commit `feat: Protection settings in the app`.

### Task 7: Installer, README, version, real-PC checks

**Files:**
- Modify: `installer/PatchLight.iss` ([Run] `--refresh-daily-check`, runhidden, before the "Open PatchLight" entry), `README.md`, `Directory.Build.props` (0.5.0)
- [ ] Full `dotnet test`; publish build compiles.
- [ ] Real PC (elevated helper via the built exe): set wait 7 → `DeferQualityUpdatesPeriodInDays = 7` present; set wait 0 → removed. Task XML check via `DailyTask.BuildXml`.
- [ ] Commit `docs: protection in README, installer refreshes the task, 0.5.0`.
