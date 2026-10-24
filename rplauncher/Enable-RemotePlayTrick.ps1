<#
  Enable-RemotePlayTrick.ps1 - lets Steam offer "Remote Play Together" for the co-op start of Deep Rock Galactic: Survivor.

  Steam offers the invite only for games that have the feature (Spacewar, app 480, has it). This puts a tiny launcher (RpLauncher.exe, 5 KB)
  in place of Spacewar's exe: starting Spacewar from Steam then starts DRG Survivor with the co-op options, and Steam shows Remote Play Together.
  Nothing of Spacewar is deleted; its exe is kept as <name>.lcbackup. DRG Survivor is not touched.

  Usage:   .\Enable-RemotePlayTrick.ps1            # install
           .\Enable-RemotePlayTrick.ps1 -Undo      # put Spacewar's exe back
  Optional: -DonorDir <folder of Spacewar>  -GameDir <folder of DRG Survivor>  -DonorExe <exe name inside the donor folder>
#>
param([switch]$Undo, [string]$DonorDir = '', [string]$GameDir = '', [string]$DonorExe = '')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

function Find-App($appId) {
  $libs = New-Object System.Collections.ArrayList
  $steam = $null
  try { $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
  if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
  $steam = $steam -replace '/', '\'
  [void]$libs.Add($steam)
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { [void]$libs.Add(($m.Groups[1].Value -replace '\\\\', '\')) } }
  foreach ($lib in $libs) {
    $mf = Join-Path $lib "steamapps\appmanifest_$appId.acf"
    if (Test-Path $mf) {
      $m = [regex]::Match((Get-Content $mf -Raw), '"installdir"\s+"([^"]+)"')
      if ($m.Success) { $d = Join-Path $lib ('steamapps\common\' + $m.Groups[1].Value); if (Test-Path $d) { return $d } }
    }
  }
  return $null
}

if (-not $DonorDir) { $DonorDir = Find-App '480' }
if (-not $DonorDir) { throw 'Spacewar not found. Install it first (Win+R: steam://install/480) or pass -DonorDir.' }
if (-not $DonorExe) {
  if (Test-Path (Join-Path $DonorDir 'SteamworksExample.exe')) { $DonorExe = 'SteamworksExample.exe' }
  else {
    $exes = @(Get-ChildItem $DonorDir -Filter *.exe | Where-Object { -not $_.PSIsContainer -and $_.Name -notmatch 'unins|crash|redist' })
    if ($exes.Count -eq 1) { $DonorExe = $exes[0].Name }
    else { throw ('Spacewar''s exe is ambiguous. ' + $DonorDir + ' contains: ' + (($exes | ForEach-Object { $_.Name }) -join ', ') + '. Pass -DonorExe <name>.') }
  }
}
$exePath = Join-Path $DonorDir $DonorExe
$bak = $exePath + '.lcbackup'
Write-Host ('Donor: ' + $exePath)

if ($Undo) {
  if (-not (Test-Path $bak)) { throw ('No backup found (' + $bak + '). Nothing to do, or in Steam: Spacewar -> Properties -> Installed files -> Verify integrity.') }
  Copy-Item $bak $exePath -Force
  Remove-Item $bak -Force
  foreach ($f in 'rp-target.txt', 'rp-launcher.log') { $p = Join-Path $DonorDir $f; if (Test-Path $p) { Remove-Item $p -Force } }
  Write-Host 'Restored: Spacewar''s original exe is back.'
  return
}

if (-not $GameDir) { $GameDir = Find-App '2321470' }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'DRG Survivor.exe'))) { throw 'DRG Survivor not found. Pass -GameDir <folder>.' }
$launcher = Join-Path $here 'RpLauncher.exe'
if (-not (Test-Path $launcher)) { throw 'RpLauncher.exe is missing next to this script.' }
if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) { throw 'The game is running. Please close it.' }
if (-not (Test-Path (Join-Path $GameDir 'Start-Coop.bat'))) { Write-Host 'Note: Start-Coop.bat is missing in the game folder; run Install-LocalCoop.bat first, or the mod is missing.' }

if (-not (Test-Path $bak)) { Copy-Item $exePath $bak; Write-Host ('Backed up: ' + $bak) } else { Write-Host 'Backup already exists, kept.' }
Copy-Item $launcher $exePath -Force
Set-Content -Path (Join-Path $DonorDir 'rp-target.txt') -Encoding ASCII -Value @((Join-Path $GameDir 'DRG Survivor.exe'), '--doorstop-enabled true -lccoop -lcremote')
Write-Host ''
Write-Host 'Done. Now start "Spacewar" in Steam (not the game itself): DRG Survivor opens with LOCAL CO-OP.'
Write-Host 'Then Shift+Tab -> Friends -> Remote Play Together.'
Write-Host 'Set the controller layout of Spacewar to "Gamepad" in Steam, or the game sees no controller.'
Write-Host ('Log: ' + (Join-Path $DonorDir 'rp-launcher.log'))
Write-Host 'To undo: run this script with -Undo (or Enable-RemotePlayTrick-Undo.bat).'
