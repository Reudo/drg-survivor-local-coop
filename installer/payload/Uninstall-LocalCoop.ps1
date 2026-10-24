<#
  Uninstall-LocalCoop.ps1 - undoes Install-LocalCoop.ps1 (lives in the game folder next to localcoop-install.json).
  Removes the plugin, Start-Coop.bat, the desktop shortcuts, the Remote Play (Spacewar) trick, the install log and the game's co-op log files,
  puts doorstop_config.ini, the BepInEx console setting and the old network mod back as they were, and asks whether to remove BepInEx too (-Yes: only if the installer put it)
  there. Saves are never touched. Uninstall-LocalCoop.bat deletes this script and itself afterwards.
  Parameters: -Yes (no questions)
  Exit code: 0 = removed, 1 = error, 2 = cancelled (the .bat deletes the scripts only after exit code 0).
#>
param([switch]$Yes)
$ErrorActionPreference = 'Stop'
function Say($t) { Write-Host $t }
function Ask($q) { if ($Yes) { return $true }; $a = Read-Host ($q + ' [Y/n]'); return ($a -eq '' -or $a -match '^[jJyY]') }

$GameDir = $PSScriptRoot
$js = Join-Path $GameDir 'localcoop-install.json'
if (-not (Test-Path $js)) { throw 'localcoop-install.json is missing: nothing to remove (or not started from the game folder).' }
if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) { throw 'The game is running. Please close it.' }
$st = Get-Content $js -Raw | ConvertFrom-Json
if (-not (Ask 'Remove local co-op again?')) { Say 'Cancelled.'; exit 2 }

# the Remote Play (Spacewar) trick first: it needs Enable-RemotePlayTrick.ps1, which is deleted below
$undo = Join-Path $GameDir 'Enable-RemotePlayTrick.ps1'
if ($st.spacewarDir -and (Test-Path $undo)) {
  $exeBak = @(Get-ChildItem $st.spacewarDir -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*.lcbackup' })
  if ($exeBak.Count -gt 0) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $undo -Undo -DonorDir $st.spacewarDir | Out-Null
    if ($LASTEXITCODE -eq 0) { Say 'Spacewar restored (its original exe is back).' }
    else { Say 'WARNING: Spacewar could not be restored. In Steam: Spacewar -> Properties -> Installed files -> Verify integrity.' }
  }
}

Remove-Item (Join-Path $GameDir 'BepInEx\plugins\LocalCoop.dll') -ErrorAction SilentlyContinue
foreach ($n in 'Start-Coop.bat', 'Start-Coop-RemotePlay.bat', 'Start-Coop-Direct.bat', 'RpLauncher.exe', 'Enable-RemotePlayTrick.ps1', 'Enable-RemotePlayTrick.bat', 'Enable-RemotePlayTrick-Undo.bat', 'lc.log', 'lc-state.jsonl', 'localcoop-settings.txt') {
  Remove-Item (Join-Path $GameDir $n) -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $GameDir 'BepInEx\config\localcoop-settings.txt') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $GameDir 'lc-shots') -Recurse -Force -ErrorAction SilentlyContinue
foreach ($l in @($st.shortcuts)) { if ($l) { Remove-Item $l -ErrorAction SilentlyContinue } }

foreach ($n in @($st.movedOldMod)) {
  if (-not $n) { continue }
  $f = Join-Path $GameDir ('BepInEx\plugins-disabled\' + $n)
  if (Test-Path $f) { Move-Item $f (Join-Path $GameDir ('BepInEx\plugins\' + $n)) -Force; Say ('Old network mod put back: ' + $n) }
}
$pd = Join-Path $GameDir 'BepInEx\plugins-disabled'
if ((Test-Path $pd) -and @(Get-ChildItem $pd -Force).Count -eq 0) { Remove-Item $pd -Force }

$ini = Join-Path $GameDir 'doorstop_config.ini'
if (Test-Path $ini) {
  $val = if ($st.doorstopWasEnabled) { 'true' } else { 'false' }
  $lines = Get-Content $ini | ForEach-Object { if ($_ -match '^\s*enabled\s*=') { 'enabled = ' + $val } else { $_ } }
  Set-Content -Path $ini -Value $lines -Encoding ASCII
  Say ('doorstop_config.ini: enabled = ' + $val + ' (as before the installation)')
}

if ($st.consoleWas -and -not $st.bepinexInstalledByUs) {
  $cfgFile = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
  if (Test-Path $cfgFile) {
    $sec = ''
    $lines = Get-Content $cfgFile | ForEach-Object {
      if ($_ -match '^\s*\[(.+)\]\s*$') { $sec = $Matches[1] }
      if ($sec -eq 'Logging.Console' -and $_ -match '^\s*Enabled\s*=') { 'Enabled = ' + $st.consoleWas } else { $_ }
    }
    Set-Content -Path $cfgFile -Value $lines -Encoding ASCII
    Say ('BepInEx.cfg: console Enabled = ' + $st.consoleWas + ' (as before the installation)')
  }
}

if (Test-Path (Join-Path $GameDir 'BepInEx')) {
  # BepInEx is also offered when it was there before the installer: other mods in BepInEx\plugins would go with it. -Yes only removes what the installer brought.
  $ours = [bool]$st.bepinexInstalledByUs
  $remove = $false
  if ($Yes) { $remove = $ours }
  elseif ($ours) { $remove = Ask 'BepInEx was brought by the installer. Remove it too?' }
  else {
    $others = @(Get-ChildItem (Join-Path $GameDir 'BepInEx\plugins') -Recurse -Filter *.dll -ErrorAction SilentlyContinue)
    $a = Read-Host ('BepInEx was already there before the installation. Remove it too? ' + $others.Count + ' other plugin file(s) in BepInEx\plugins would be deleted with it. [y/N]')
    $remove = ($a -match '^[jJyY]')
  }
  if ($remove) {
    foreach ($n in 'BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') {
      $p = Join-Path $GameDir $n
      if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Say 'BepInEx removed.'
  } else { Say 'BepInEx kept.' }
}
Remove-Item $js -ErrorAction SilentlyContinue
Remove-Item (Join-Path $GameDir 'localcoop-install.log') -ErrorAction SilentlyContinue
Say 'Done. Saves were not touched. (Uninstall-LocalCoop.bat now deletes the scripts.)'
exit 0
