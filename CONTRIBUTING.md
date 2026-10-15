# Contributing to PatchLight

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting a problem

Open an [issue](https://github.com/G4SBO/PatchLight/issues) and include:
- your Windows edition and build (Settings → System → About),
- what PatchLight showed, and what you expected,
- the output of `patchlight` in a terminal, if you can.

If an update got the wrong light, link Microsoft's known issue (or the news article) so it can be checked.

## Making a change

1. Install the .NET 10 SDK on Windows 11. No Visual Studio needed.
2. Run `dotnet test` before and after your change.
3. Try the app safely with `dotnet run --project src/PatchLight.App -- --demo` (made-up updates, nothing on your PC changes).
4. Keep pull requests small, one change each, and add a test when you change how updates are judged.

Good places to start: issues labeled [good first issue](https://github.com/G4SBO/PatchLight/labels/good%20first%20issue).

## Ground rules

- The app is English only, plain words, no jargon in the UI.
- No account, no analytics, no server: PatchLight only reads public pages. New network sources must be free and public.
- Never block updates for good: every pause has to end on its own.
