<#
  new-test-copy.ps1 - makes the test copy of the game that all other scripts use (<work>\game-lc, see lc-paths.ps1).

  What it does, in order:
    1. copies the game folder to <work>\game-lc (the original is only read, never changed), leaving out anything of BepInEx / earlier mods
    2. unpacks BepInEx 6 (be.789, IL2CPP) into the copy; the zip is checked against the pinned sha256 first
    3. writes the BepInEx settings that work with Unity 6 (blank base-library source) and switches its console off
    4. starts the copy once, so that BepInEx generates the interop assemblies (the plugin is compiled against them), waits until they are there
       and closes that one game window again (only the process this script started)

  Usage:   .\new-test-copy.ps1 -SteamGameDir "D:\SteamLibrary\steamapps\common\<game folder>" -BepInExZip "<path>\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789+6cea286.zip"
           (-BepInExZip may be left out when the zip lies in <work>\downloads; -SteamGameDir may be left out when Steam's library lists the game)

  The first start takes a few minutes. Afterwards:   .\lc-run.ps1 -Deploy      (SETUP.md has the whole story)
#>
param(
  [string]$SteamGameDir = '',
  [string]$BepInExZip = '',
  [switch]$Force,
  [int]$InteropWaitSeconds = 600
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'lc-paths.ps1')

$PinnedSha256 = '29519614d0121a3eefc9df6d61436f0c09a2c5f85b0a7d92559a135ce3055eb1'      # BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789+6cea286.zip

# ---- the game folder --------------------------------------------------------------------------------------------------
function Find-SteamGame {
  $libs = @()
  try {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
    $libs += $steam
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace '\\\\', '\') } }
  } catch { }
  foreach ($l in ($libs | Select-Object -Unique)) {
    $common = Join-Path $l 'steamapps\common'
    if (-not (Test-Path $common)) { continue }
    foreach ($d in Get-ChildItem $common -Directory -ErrorAction SilentlyContinue) {
      if (Test-Path (Join-Path $d.FullName 'DRG Survivor.exe')) { return $d.FullName }
    }
  }
  return ''
}
if (-not $SteamGameDir) { $SteamGameDir = Find-SteamGame }
if (-not $SteamGameDir -or -not (Test-Path (Join-Path $SteamGameDir 'DRG Survivor.exe'))) { throw 'the game folder was not found: pass -SteamGameDir "<folder with DRG Survivor.exe>"' }
$SteamGameDir = (Resolve-Path $SteamGameDir).Path
if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) { throw 'a DRG Survivor process is running - close it first (this script never kills a game it did not start)' }
if ((Test-Path $LcGame) -and -not $Force) { throw "the test copy exists already: $LcGame (use -Force to build it again; the old one is renamed, not deleted)" }

# ---- BepInEx zip, checked ---------------------------------------------------------------------------------------------
if (-not $BepInExZip) {
  $c = Get-ChildItem $LcDownloads -ErrorAction SilentlyContinue -Filter 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789*.zip' | Select-Object -First 1
  if ($c) { $BepInExZip = $c.FullName }
}
if (-not $BepInExZip -or -not (Test-Path $BepInExZip)) { throw "BepInEx zip not found. Download BepInEx 6.0.0-be.789 (Unity IL2CPP, win-x64) from https://builds.bepinex.dev/projects/bepinex_be and pass it with -BepInExZip, or put it in $LcDownloads. Expected sha256: $PinnedSha256" }
$sha = (Get-FileHash $BepInExZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sha -ne $PinnedSha256) { throw "the BepInEx zip has the wrong sha256 ($sha, expected $PinnedSha256): not used" }
Write-Host "BepInEx zip checked: $BepInExZip"

# ---- copy ---------------------------------------------------------------------------------------------------------------
if (Test-Path $LcGame) { $old = "$LcGame.old-" + (Get-Date -Format 'yyyyMMdd-HHmmss'); Rename-Item $LcGame $old; Write-Host "the old test copy was renamed to $old" }
New-Item -ItemType Directory -Force (Split-Path $LcGame) | Out-Null
Write-Host "copying $SteamGameDir -> $LcGame (about 3.5 GB) ..."
& robocopy $SteamGameDir $LcGame /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 `
  /XD BepInEx dotnet lc-shots `
  /XF doorstop_config.ini winhttp.dll lc.log lc-state.jsonl localcoop-settings.txt localcoop-install.log Start-Coop.bat 'Enable-RemotePlayTrick*.bat' 'Uninstall-LocalCoop.bat' | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $LcGame 'DRG Survivor.exe'))) { throw 'the copy has no DRG Survivor.exe' }

# ---- BepInEx ------------------------------------------------------------------------------------------------------------
Expand-Archive -Path $BepInExZip -DestinationPath $LcGame -Force
$cfgDir = Join-Path $LcGame 'BepInEx\config'
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Set-Content -Path (Join-Path $cfgDir 'BepInEx.cfg') -Encoding ASCII -Value @(
  '[IL2CPP]', 'UpdateInteropAssemblies = true', 'UnityBaseLibrariesSource = ', '', '[Logging.Console]', 'Enabled = false')
if (-not (Test-Path (Join-Path $LcGame 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))) { throw 'BepInEx was not unpacked correctly' }
Write-Host 'BepInEx unpacked and configured.'

# ---- first start: the interop assemblies ----------------------------------------------------------------------------
$asm = Join-Path $LcGame 'BepInEx\interop\Assembly-CSharp.dll'
if (-not (Test-Path $asm)) {
  Write-Host 'starting the test copy once so that BepInEx generates the interop assemblies (a few minutes) ...'
  $p = Start-Process -FilePath (Join-Path $LcGame 'DRG Survivor.exe') -WorkingDirectory $LcGame -ArgumentList '-screen-fullscreen 0 -screen-width 960 -screen-height 540' -PassThru
  $t0 = Get-Date; $lastSize = -1; $stable = 0
  while (((Get-Date) - $t0).TotalSeconds -lt $InteropWaitSeconds) {
    Start-Sleep -Seconds 5
    if ($p.HasExited) { break }
    if (Test-Path $asm) {
      $s = (Get-Item $asm).Length
      if ($s -eq $lastSize) { $stable++ } else { $stable = 0; $lastSize = $s }
      if ($stable -ge 3) { break }          # unchanged for 15 s: written completely
    }
  }
  if (-not $p.HasExited) { Start-Sleep -Seconds 10; try { Stop-Process -Id $p.Id -Force } catch { }; [void]$p.WaitForExit(20000) }
  Start-Sleep -Seconds 3
}
if (-not (Test-Path $asm)) { throw "the interop assemblies were not generated (see $LcGame\BepInEx\LogOutput.log)" }
Write-Host "done: $LcGame"
Write-Host 'next: .\lc-run.ps1 -Deploy        (builds the plugin against this copy and runs the boot test)'
