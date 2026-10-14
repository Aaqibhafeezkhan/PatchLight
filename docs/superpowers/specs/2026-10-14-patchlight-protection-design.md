# PatchLight: protection before Windows gets the update ready

Date: 2026-10-14. Status: design.

## Why

Pausing only works while an update is still waiting. Once Windows has staged it for the next restart
("Update and shut down" is the only choice in the power menu), hiding it does nothing, and we tested on a
real PC that `shutdown /s /t 0` installs it anyway. So PatchLight has to act earlier. Three changes:

1. **Auto-pause red updates** (optional): the background check pauses updates with confirmed problems
   without anyone clicking.
2. **Check every hour**: the background check runs hourly instead of daily, so it usually gets to an
   update before Windows stages it.
3. **Wait before new updates** (optional): Windows doesn't offer a new monthly update until it is a few
   days old, so problems have time to show up in Microsoft's list. A brand-new update almost never has
   known problems on day one, which is why 1 and 2 alone can't stop it.

All three default to off or to today's behavior except the hourly check, which only changes timing.

## 1. Auto-pause red updates

- New setting "Pause red updates automatically", default off.
- Applied only by the background check (runs as SYSTEM), never by the tray app, so no UAC prompts.
- After its usual release step, the background check pauses every update whose light is **red**, that is
  not hidden and has a `PauseKey`, and that has no existing hold. It uses `PatchLightService.HoldAsync`
  with the person's pause length and reason "Paused automatically", so the pause ends exactly like a
  manual one (fixed, expired, or resumed by hand).
- Yellow is never auto-paused; news reports can't make an update red, so news never triggers it.
- An update resumed by hand is not auto-paused again while it stays the same update with the same
  problems: the background check remembers (in `settings.json`, list `ResumedByHand`) the KB + issue ids
  of manual resumes and skips those. A new problem id on it, or a new KB, makes it eligible again.
- `BackgroundCheckLog` gets a `Paused` list. The app turns new entries into one notification:
  "PatchLight paused KB5127648 automatically: <first problem title>".

## 2. Hourly background check

- The scheduled task (same name, `\PatchLight\Daily check`, so nothing to migrate in Task Scheduler)
  gets `<Repetition><Interval>PT1H</Interval><Duration>P1D</Duration></Repetition>` on its daily
  trigger. The boot trigger stays.
- The check reads one Microsoft page (cached when unchanged) and asks Windows Update for pending
  updates. Background checks still don't read tech news.
- UI text says "Automatic check (every hour)" instead of "Daily automatic check".
- Existing installs: the installer runs `PatchLight.exe --refresh-daily-check` (as admin, after files are
  copied), which re-registers the task only if it already exists. Zip users see a new state
  `DailyCheckState.Outdated` ("Turn it off and on to switch to hourly checks"); any elevated action that
  ensures the task (pause, uninstall, settings change) also re-registers an outdated task.
  Outdated = the registered task XML has no `PT1H` repetition.

## 3. Wait before new updates

- New setting "Wait before installing new updates": Off / 3 / 7 / 14 days, default Off.
- **Pro, Enterprise, Education** (`EditionID` not `Core*`): set the Windows Update policy
  `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` `DeferQualityUpdates = 1`,
  `DeferQualityUpdatesPeriodInDays = N`. Windows itself then doesn't offer the update until N days after
  release, so nothing can race it. PatchLight records that it owns these values
  (`settings.json`, `OwnsDeferPolicy = true`). If the values already exist and PatchLight didn't set them
  (a company policy), the setting shows "Managed by your organization" and is disabled. Turning it off
  deletes only values PatchLight owns. Settings will show Windows' own "Some settings are managed by your
  organization" banner; the PatchLight setting explains this in one line.
- **Home** (`EditionID` starts with `Core`): the policy isn't supported, so the hourly background check
  pauses any OS update younger than N days (from `PendingUpdate.Released`) with reason
  "Waiting N days after release", expiring at release + N days. When it expires, the normal release
  step resumes it, and auto-pause (if on) catches it then if it turned red. The UI says honestly that on
  Home this works best when the PC is on and online, because Windows may get the update ready first.
- Uninstall cleanup removes PatchLight-owned policy values.

## Settings storage

- New `C:\ProgramData\PatchLight\settings.json` (folder already admin-only via `DataFolder.Secure`):
  `{ AutoPauseRed, WaitDays, PauseDays, OwnsDeferPolicy, ResumedByHand }`. Read by the background check;
  written only by the elevated helper.
- New elevated verb: `--set-protection --auto-pause on|off --wait-days N --days N --result <file>`.
  It writes `settings.json`, applies/removes the defer policy, and ensures the hourly task (the protection
  settings need it). One UAC prompt per change.
- The app shows the current values by reading `settings.json` (readable by users).
- Manual resume (`ReleaseAsync` via the elevated helper) adds to `ResumedByHand`.

## Settings page

A new card "Protection" above the existing toggles:
- Toggle "Pause red updates automatically" + one line: "The hourly check pauses updates with confirmed
  problems. They resume when Microsoft fixes them."
- Combo "Wait before installing new updates" (Off / 3 / 7 / 14 days) + one line per edition.
- Both disabled with "Turn on the automatic check first" text if the task is off.

## Testing

- Unit tests (fakes, like the existing service tests): red gets auto-paused; yellow/green don't; already
  held don't; resumed-by-hand with same issue ids don't, with a new issue id do; Home wait-hold created
  for young OS updates only, expires at release + N; expired wait-hold then red → auto-paused in the
  same run.
- `DailyTask.BuildXml` contains the hourly repetition; `IsOutdated` detects old XML.
- Defer policy writer: writes, refuses when foreign values exist, removes only owned values (registry
  access behind a small interface so it is testable).
- `ElevatedCommand.Parse` for the new verb.
- Real-PC test on LO's PC (Pro): turn on wait 7 days → Windows Update advanced options lists the
  configured policy; turn off → gone. Auto-pause and hourly task verified in demo mode and by checking
  the registered task.

## Out of scope

- Cancelling an update that is already staged (no reliable way; tested).
- Auto-pausing yellow updates.
- Feature upgrades (26H2) for the wait setting: they're already optional and judged separately.
