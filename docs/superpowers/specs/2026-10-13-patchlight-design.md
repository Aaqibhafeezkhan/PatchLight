# PatchLight — Design

**Date:** 2026-10-13
**One-liner:** Know if a Windows update is safe *before* you install it. 🟢🟡🔴

## Problem

Windows updates regularly break PCs (black screens, Remote Desktop failures, broken audio).
People only find out after the damage is done. Existing "pause updates" tools pause blindly;
sites that list known issues (Patch Scanner, AskWoody) don't look at *your* PC and don't act.

## What PatchLight does

1. Reads the updates Windows Update has waiting for this PC.
2. Cross-checks each one against Microsoft's public known-issues list for the PC's Windows 11
   version (`learn.microsoft.com/en-us/windows/release-health/status-windows-11-<version>`).
3. Gives each update a light:
   - 🔴 **Problems** — an open (Confirmed / Investigating) issue affects it.
   - 🟡 **Wait** — an issue with a workaround (Mitigated / unknown status) affects it, or it is an
     OS update released less than 3 days ago.
   - 🟢 **Safe** — no known problems.
4. Lets the user pause (hide) only the bad updates with one action. A paused update is resumed
   automatically when it turns green, when the pause time ends (default 14 days, max 35), or is
   dropped when Windows replaces it with a newer update. PatchLight never blocks forever.
5. Shows recently installed updates and marks the one most likely to have caused an open problem.

## Decisions (from the 2026-10-13 Q&A)

| Topic | Decision |
|---|---|
| Form | Windows app (window + tray icon with live light) is the main experience; a CLI (`patchlight`) is also available |
| Automation | Suggest + one-click pause by default; full auto mode as a setting later |
| Tech | C# / .NET 10. Engine + CLI first, WinUI 3 app on top of the same engine |
| Language | English UI, plain everyday words |
| Platform | Windows 11 only (detect by `CurrentBuild >= 22000`; the registry `ProductName` wrongly says "Windows 10") |
| Name | PatchLight |
| Cost | Free, open source (MIT), no server: each PC fetches Microsoft's public page itself |

## Key facts verified on 2026-10-13

- Release-health pages are public HTML. The summary table (`<thead>` with "Summary" and
  "Originating update") has one row per issue: title in `<b>`, issue id in `<div id="NNNNmsg">`,
  "OS Build 26200.9550 / KB5124010 / 2026-09-22", status ("Resolved KB5127648", "Mitigated", ...).
- The Markdown source repo (`MicrosoftDocs/windows-release-pr`) is private; the Graph API needs a
  business tenant. HTML scraping is the only free route.
- Windows Update titles look like `2026-10 Security Update (KB5127648) (26200.9711)`. The build
  number in the title is how we know an OS update inherits problems from earlier builds
  (updates are cumulative: build ≥ originating build ⇒ affected until resolved).
- Hiding/unhiding via the Windows Update Agent COM API (`Microsoft.Update.Session`,
  `IUpdate.IsHidden`) needs administrator rights; searching does not.

## Roadmap (one plan each)

1. **Engine + CLI** — parser, lights, pause/resume, history, `patchlight` command. *(Plan 1)*
2. **Windows app** — WinUI 3 window, tray icon that shows the light, notifications, daily
   background check (scheduled task) that auto-resumes fixed updates.
3. **"What broke my PC?"** — guided uninstall (DISM for combined cumulative packages), filter out
   issues that only hit businesses (Azure Virtual Desktop, domain, Server).
4. **Community signal + release** — BleepingComputer RSS and other free community sources
   (verify each is genuinely free first), installer (winget/MSIX), README GIF, GitHub release.
