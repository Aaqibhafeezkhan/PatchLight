# PatchLight Plan 3: "What broke my PC?" + business-only filter

**Goal:** Let people remove the update that most likely broke their PC (and keep it away until Microsoft fixes it), and stop counting problems that only hit business PCs on home PCs. Also clear the Plan 2 deferred minors and show the author (G4SBO).

**Spec:** `docs/superpowers/specs/2026-10-13-patchlight-design.md`, roadmap item 3.

## Decisions (2026-10-14, LO said "go ahead", author = G4SBO)
- **Business-only is set aside, never hidden.** An issue is "likely business-only" only on strong signals in its title, summary or first detail paragraph: domain-joined / Active Directory / domain controller, Azure Virtual Desktop, Windows 365, virtual desktop environments, Remote Desktop Services, Windows Server, WSUS / Configuration Manager / Intune, Administrator protection, "managed devices", or Microsoft's "Home users ... unlikely" sentence. Workaround/resolution paragraphs are ignored (they mention Group Policy and IT admins for every issue). Set-aside issues are still listed, marked "usually only affects business PCs", and don't color the light.
- **Who sees business issues:** PCs joined to a domain or to Microsoft Entra count everything. Others set them aside. The app has a switch to count them anyway. The daily SYSTEM check uses the PC's own join state.
- **Uninstall:** Windows cumulative updates are removed with DISM (`Package_for_RollupFix` whose version revision equals the update's build revision); other updates with `wusa /uninstall /kb:`. Needs administrator. Only the newest cumulative update can be removed; otherwise a plain message.
- **Keep it away:** after removing, PatchLight records a waiting pause (a hold with no update id yet). When Windows offers that KB again, the next administrator check (daily task, now also 5 minutes after start-up) hides it. Waiting pauses expire like any pause.
- **Restart:** the app offers "Restart now"; nothing restarts by itself.
- Minors from Plan 2: Settings "moved" text; fixed-update alert honest when the daily check is off; an ended pause reads "Pause ended, press Resume".

## Tasks
1. Parser reads each issue's description and classifies business-only (KnownIssue.LikelyBusinessOnly). Fixture tests.
2. VerdictEngine sets business-only issues aside (UpdateVerdict.SetAside) unless told to count them; service option; SystemInfo.IsWorkPc.
3. Waiting pauses: Hold with empty UpdateId; CheckAsync attaches/hides when the KB appears; expiry; ReleaseAll skips hiding.
4. UpdateRemover: DISM package list parser (pure), remove via DISM/wusa, elevated `--uninstall-update`, CLI `uninstall`, task boot trigger.
5. App: History "Uninstall this update" with confirmation + restart offer; set-aside display; Settings switch; minors; G4SBO in About, README, installer.
6. README, version 0.3.0, CI, final review.
