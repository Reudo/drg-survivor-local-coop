<#
  rp-handtest.ps1 - hand test: does Steam Remote Play Together work under the game's own AppID (no Spacewar)?

  The owner starts the TEST COPY through Steam (the overlay is only there when Steam starts the game). Steam setting, DRG Survivor ->
  Properties -> Launch options (undo: empty the field):
      "<test copy>\DRG Survivor.exe" -lccoop -lcremote -lcrpdialog %command%
  This script backs up the save profile, waits until that game has started and closed again, restores the saves byte for byte and
  prints the plugin's "rp:" lines (Steam state, the invite dialog, Remote Play sessions with the guest's device type).

  Usage: .\rp-handtest.ps1        (then start DRG Survivor in Steam, open the invite with F8 or Shift+Tab, play, close the game)
         .\rp-handtest.ps1 -Launch (starts the test copy itself, without Steam: no overlay, so invite through rp-friends.txt / rp-invite.txt)
#>
param([switch]$Launch)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$game = (Resolve-Path $LcGame).Path
if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) { throw 'a DRG Survivor process is running - close it first' }

$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Funday Games\DRG Survivor\steam'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $LcRuns ("{0}-rp-handtest" -f $stamp)
$backup = Join-Path $LcBackup ("lc-saves-{0}-rp-handtest" -f $stamp)
New-Item -ItemType Directory -Force $runDir, $backup | Out-Null
if (Test-Path $saveDir) { Copy-Item (Join-Path $saveDir '*') $backup -Recurse -Force }
Write-Host ('saves: backed up to {0}' -f $backup)
$lcLog = Join-Path $game 'lc.log'
Remove-Item (Join-Path $game 'rp-friends.txt'), (Join-Path $game 'rp-invite.txt') -Force -ErrorAction SilentlyContinue
if ($Launch) {
  Start-Process -FilePath (Join-Path $game 'DRG Survivor.exe') -ArgumentList @('-lccoop', '-lcremote', '-lcrpdialog') -WorkingDirectory $game | Out-Null
} else {
  Write-Host ''
  Write-Host 'Steam: DRG Survivor -> Properties -> Launch options:'
  Write-Host ('    "{0}" -lccoop -lcremote -lcrpdialog %command%' -f (Join-Path $game 'DRG Survivor.exe'))
  Write-Host 'Then start DRG Survivor in Steam. Waiting for the game ...'
}

$proc = $null
while (-not $proc) { Start-Sleep -Seconds 2; $proc = Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue | Select-Object -First 1 }
$path = try { $proc.Path } catch { '?' }
Write-Host ('game started: {0}' -f $path)
if ($path -ne (Join-Path $game 'DRG Survivor.exe')) { Write-Host 'NOTE: this is not the test copy - the launch option was not used. The saves are still restored afterwards.' }
Write-Host 'Waiting until the game is closed ...'
$proc.WaitForExit()
Start-Sleep -Seconds 3

if (Test-Path $lcLog) { Copy-Item $lcLog $runDir }
Remove-Item (Join-Path $game 'rp-friends.txt'), (Join-Path $game 'rp-invite.txt') -Force -ErrorAction SilentlyContinue     # friends' names stay out of the run folder
Get-ChildItem $saveDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $backup '*') $saveDir -Recurse -Force
$a = Get-ChildItem $backup -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName).Hash }
$b = Get-ChildItem $saveDir -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName).Hash }
$restore = if (($a -join ',') -eq ($b -join ',')) { 'identical' } else { 'DIFFERENT' }

$lines = @()
if (Test-Path (Join-Path $runDir 'lc.log')) { $lines = Select-String -Path (Join-Path $runDir 'lc.log') -Pattern 'plugin loaded|rp:|input: gamepads' | ForEach-Object { $_.Line } }
$summary = @("run: $runDir", "game: $path", "saves restore: $restore") + $lines
$summary | Set-Content (Join-Path $runDir 'result.txt')
$summary | ForEach-Object { Write-Host $_ }
