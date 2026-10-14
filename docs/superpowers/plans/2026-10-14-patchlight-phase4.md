# PatchLight Plan 4: new Windows versions, community signal, distribution

**Spec:** `docs/superpowers/specs/2026-10-13-patchlight-design.md`, roadmap item 4. LO said "sigue con la fase 4" (2026-10-14).

## Decisions
- **Upgrades** ("Windows 11, version 26H2") are optional offers in the Upgrades classification
  (3689bdc8-b205-4af4-8d4a-a63924c5e9d5), found with `DeploymentAction='OptionalInstallation'`. Windows gives them the same
  KB as that month's security update, so they are keyed by version (pause with `26H2`). Every open issue on the target
  version's page applies. An unreadable page means "wait", never a failed check, and never ends a pause.
- **Community signal:** public RSS of Neowin and Windows Latest (free, no key). BleepingComputer's feed was checked and
  rejected: 15 items across all topics. A headline must report a problem and not a fix; the KB may be in the title or the
  article text. News can turn green into yellow, never red, and never decides when a pause ends. Reports kept 30 days in
  %LOCALAPPDATA%\PatchLight\news.json. Switch in Settings, `--no-news` in the CLI.
- **Settings page refresh:** after a pause or resume the elevated helper runs `UsoClient StartScan`.
- **winget:** `scripts/winget-manifest.ps1 -Version x.y.z` writes validated manifests (G4SBO.PatchLight). Submitting the
  PR to microsoft/winget-pkgs is LO's call.
- **Signing (SignPath):** waits for some public traction (see README Code signing policy).
- Cheap Plan 3 minors fixed here: hex error codes, one uninstall at a time, no second uninstall for a removed update,
  safe restart button, tests keep their error log in a temp folder.
