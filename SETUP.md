# Setup: from a fresh clone to a green test run

You need the game (Steam), Windows 10/11, PowerShell 5.1 and the .NET SDK (6 or newer; the plugin targets net6.0 and was built with SDK 10). Nothing in this repo
contains game files; the plugin is compiled against assemblies that BepInEx generates from **your** copy of the game.

## 1. Clone
```
git clone https://github.com/Reudo/drg-survivor-local-coop
cd drg-survivor-local-coop
```

## 2. Get BepInEx (pinned)
Download **BepInEx 6.0.0-be.789, Unity IL2CPP, win-x64** (`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789+6cea286.zip`) from the official build server
<https://builds.bepinex.dev/projects/bepinex_be> and check its SHA-256:

```
29519614d0121a3eefc9df6d61436f0c09a2c5f85b0a7d92559a135ce3055eb1
```
Put the zip in `work\downloads\` (create the folder), or pass its path to the next script. The scripts refuse a zip with another hash.

## 3. Make the test copy of the game
```
powershell -ExecutionPolicy Bypass -File tools\new-test-copy.ps1
```
It finds the game through Steam's library list (or pass `-SteamGameDir "<folder with DRG Survivor.exe>"`), copies it to `work\game-lc` (about 3.5 GB;
the original is only read), unpacks BepInEx into the copy, writes the BepInEx settings that work with Unity 6, and starts the copy once so that BepInEx
generates the interop assemblies (a few minutes). **All scripts work on this copy only. Never run them against your Steam install.**

`work\` is git-ignored. Its place is set in `tools\lc-paths.ps1`: `<repo>\work` by default, or the environment variable `LC_WORK`, or a git-ignored
`tools\lc-local.ps1` that sets any of `$LcWork`, `$LcGame`, `$LcRuns`, `$LcBackup`, `$LcRelease`, `$LcDownloads`.

## 4. First run
```
powershell -ExecutionPolicy Bypass -File tools\lc-run.ps1 -Deploy -Name first -Extra "-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest"
```
`-Deploy` builds the plugin (`dotnet build` with `-p:GameDir=<work>\game-lc`), copies it into the test copy, backs up your save profile, starts the game, clicks the
publisher splash, lets the plugin run its scripted test with two virtual gamepads, restores the saves byte for byte and prints `verdict: PASS` or `FAIL` with the
plugin's check lines. A run folder with `lc.log` and screenshots lands in `work\lc-runs\`. The script refuses to start while a game is running and never closes one.

Each check has a **twin**: a launch flag `-lcno...` that switches the feature off and must turn the check red. The batch scripts `tools\batch_*.ps1` run checks
and their twins one after the other (for example `batch_r10e.ps1`); the test flags (`-lcpausetest` and so on) are in the plugin sources (`LocalCoop\*Test.cs`, `UiTest.cs`).
`-English` makes the game itself run in English for a run (screenshots); without it the plugin's texts are English and the game's own follow your language.

## 5. Build the installer
```
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```
Needs the BepInEx zip from step 2 (in `work\downloads\`) and a built plugin. Output: `work\release\DrgSurvivorLocalCoop-1.0.1-Installer.zip`. GitHub cannot build this for
you (the plugin needs your game's generated assemblies), so a release is built on a PC with the game and uploaded by hand (`gh release create`).

## Where to start reading
- `PLAN.md`: the decisions and what was measured, round by round.
- `LocalCoop\Plugin.cs`: the per-frame order of everything; `Share.cs`: which Harmony patches are installed with the second dwarf.
- `LocalCoop\CoopMenu.cs`, `Flow.cs`, `MenuPad.cs`: the co-op menu and its controller handling.
- `LocalCoop\Phase1.cs`: the second dwarf (the clone); `Chooser.cs`: whose turn it is on shared screens; `Zones.cs`: world zones that only knew the host.

## Rules
Work on the test copy, never on the Steam install or your saves. Every check needs a twin that turns it red. A green run is not acceptance: hand checks with real
controllers are done by the owner. Do not commit game files, BepInEx, built binaries or screenshots that show private data.
