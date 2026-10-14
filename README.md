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

Pause only the bad ones. Each time you run `patchlight` as administrator, it resumes paused updates that Microsoft has fixed, and nothing stays paused for more than 35 days, because security updates matter. (An automatic daily check is coming with the Windows app.)

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
