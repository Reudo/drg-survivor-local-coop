<#
  Install-LocalCoop.ps1 - installs the local co-op mod for Deep Rock Galactic: Survivor.

  What it does (and nothing else):
   1. finds the Steam game folder (or uses -GameDir), refuses to run while the game is open
   2. backs up the save profile (Documents\DRG-Survivor-Saves-Backup-<time>)
   3. installs BepInEx 6 (be.789, IL2CPP) from payload\bepinex-base only if the game folder has none, with the Unity-6 setting that works,
      and switches the BepInEx console OFF (a second game window makes Steam Remote Play Together block the guest's input)
   4. moves the old network mod (DrgSurvivorCoopProbe.dll, LiteNetLib.dll) to BepInEx\plugins-disabled
   5. copies LocalCoop.dll to BepInEx\plugins
   6. switches BepInEx OFF in doorstop_config.ini: a normal start (Steam "Play") is the plain single-player game again
   7. writes Start-Coop.bat, a desktop shortcut and the uninstaller into the game folder
   8. Remote Play Together (optional): if Spacewar (app 480) is installed, enables the Spacewar trick (see README.txt); otherwise offers to open its Steam install page

  Start-Coop.bat switches BepInEx on for that start only (--doorstop-enabled true) and adds -lccoop.
  Parameters: -GameDir <path>  -Yes (no questions)  -NoShortcuts  -NoBackup  -SpacewarDir <path> (override / test)  -DesktopDir <path> (test)
#>
param(
  [string]$GameDir = '',
  [switch]$Yes,
  [switch]$NoShortcuts,
  [switch]$NoBackup,
  [string]$SpacewarDir = '',
  [string]$DesktopDir = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$AppId = '2321470'
$here = $PSScriptRoot
$payload = Join-Path $here 'payload'
if (-not (Test-Path $payload)) { $payload = $here }      # the single-file setup unpacks everything into one flat folder

function Say($t) { Write-Host $t }
function Ask($q) { if ($Yes) { return $true }; $a = Read-Host ($q + ' [Y/n]'); return ($a -eq '' -or $a -match '^[jJyY]') }

# ---- 1. find the game ------------------------------------------------------------------------------------------------
function Find-App($appId, $exeName) {
  $libs = New-Object System.Collections.ArrayList
  $steam = $null
  try { $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
  if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
  $steam = $steam -replace '/', '\'
  [void]$libs.Add($steam)
  $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) {
    foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { [void]$libs.Add(($m.Groups[1].Value -replace '\\\\', '\')) }
  }
  foreach ($lib in $libs) {
    $mf = Join-Path $lib "steamapps\appmanifest_$appId.acf"
    if (Test-Path $mf) {
      $m = [regex]::Match((Get-Content $mf -Raw), '"installdir"\s+"([^"]+)"')
      if ($m.Success) { $d = Join-Path $lib ('steamapps\common\' + $m.Groups[1].Value); if (Test-Path $d) { if (-not $exeName -or (Test-Path (Join-Path $d $exeName))) { return $d } } }
    }
  }
  return $null
}

Say ''
Say '=== Deep Rock Galactic: Survivor - Local Co-op: installation ==='
if (-not $GameDir) { $GameDir = Find-App $AppId 'DRG Survivor.exe' }
if (-not $GameDir -and -not $Yes) { $GameDir = Read-Host 'Game folder not found. Path to "Deep Rock Survivor" (the folder with DRG Survivor.exe)' }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'DRG Survivor.exe'))) { throw 'Game folder not found (DRG Survivor.exe is missing). Start again with -GameDir <path>.' }
$GameDir = (Resolve-Path $GameDir).Path
Say ('Game folder: ' + $GameDir)
if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) { throw 'The game is running. Close it and start the installer again.' }
# The Nexus package has no RpLauncher.exe / Remote Play trick and no BepInEx; the full package (GitHub release) has both.
$hasRp = (Test-Path (Join-Path $payload 'RpLauncher.exe')) -and (Test-Path (Join-Path $payload 'Enable-RemotePlayTrick.ps1'))
foreach ($f in 'LocalCoop.dll', 'Uninstall-LocalCoop.ps1') {
  if (-not (Test-Path (Join-Path $payload $f))) {
    $have = '(folder payload is missing)'
    if (Test-Path $payload) { $have = (@(Get-ChildItem $payload | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.Name }) -join ', ') }
    throw ("Package incomplete: payload\$f is missing. Looked in: $payload`r`nFound there: $have`r`n" +
      "Possible causes: the ZIP was not fully extracted (extract EVERYTHING and keep the payload folder), " +
      "or the virus scanner removed LocalCoop.dll (Windows Security -> protection history, allow the file, extract the ZIP again).")
  }
}
if (-not (Ask 'Install?')) { Say 'Cancelled.'; return }

$log = Join-Path $GameDir 'localcoop-install.log'
function Log($t) { Add-Content -Path $log -Value ((Get-Date -Format 's') + ' ' + $t); Say $t }
Log '--- install ---'

# ---- 2. back up the saves --------------------------------------------------------------------------------------------
$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Funday Games\DRG Survivor'
$backup = ''
if (-not $NoBackup -and (Test-Path $saveDir)) {
  $backup = Join-Path ([Environment]::GetFolderPath('MyDocuments')) ('DRG-Survivor-Saves-Backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
  Copy-Item $saveDir $backup -Recurse
  Log ('Saves backed up: ' + $backup)
} else { Log 'Saves: no backup (none found, or -NoBackup)' }

# ---- 3. BepInEx ------------------------------------------------------------------------------------------------------
$state = @{ installedAt = (Get-Date -Format 's'); bepinexInstalledByUs = $false; doorstopWasEnabled = $true; consoleWas = $null; movedOldMod = @(); backup = $backup; shortcuts = @(); spacewarDir = '' }
$core = Join-Path $GameDir 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
$hasBep = (Test-Path (Join-Path $GameDir 'winhttp.dll')) -and (Test-Path $core)
if ($hasBep) { Log 'BepInEx is already in the game folder: kept.' }
else {
  $base = Join-Path $payload 'bepinex-base'
  if (-not (Test-Path (Join-Path $base 'winhttp.dll'))) {
    throw ("BepInEx is not installed in the game folder, and this package does not include it.`r`n" +
      "Install BepInEx 6.0.0-be.789 (Unity IL2CPP, win-x64) first: https://builds.bepinex.dev/projects/bepinex_be`r`n" +
      "Unpack it into the game folder, start the game once, close it, then run this installer again.`r`n" +
      "(The GitHub release 'DrgSurvivorLocalCoop-Installer' includes BepInEx and does it for you.)")
  }
  Log 'Installing BepInEx 6 (be.789) ...'
  Copy-Item -Path (Join-Path $base '*') -Destination $GameDir -Recurse -Force
  $cfgDir = Join-Path $GameDir 'BepInEx\config'
  New-Item -ItemType Directory $cfgDir -Force | Out-Null
  $cfg = Join-Path $cfgDir 'BepInEx.cfg'
  if (-not (Test-Path $cfg)) {
    # Unity 6: the default download source for the base libraries does not work, a blank one does
    Set-Content -Path $cfg -Encoding ASCII -Value @('[IL2CPP]', 'UpdateInteropAssemblies = true', 'UnityBaseLibrariesSource = ')
  }
  $state.bepinexInstalledByUs = $true
}

# ---- 3b. BepInEx console off -----------------------------------------------------------------------------------------
# The console is a second window of the game process. With it, Steam Remote Play Together shows the guest "input disabled while the host is busy".
$cfgFile = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
if (Test-Path $cfgFile) {
  $out = New-Object System.Collections.ArrayList; $sec = ''; $done = $false
  foreach ($ln in @(Get-Content $cfgFile)) {
    if ($ln -match '^\s*\[(.+)\]\s*$') { $sec = $Matches[1] }
    if ($sec -eq 'Logging.Console' -and $ln -match '^\s*Enabled\s*=\s*(\w+)') { $state.consoleWas = $Matches[1]; $ln = 'Enabled = false'; $done = $true }
    [void]$out.Add($ln)
  }
  if (-not $done) { [void]$out.Add(''); [void]$out.Add('[Logging.Console]'); [void]$out.Add('Enabled = false') }
  Set-Content -Path $cfgFile -Value $out -Encoding ASCII
} else {
  New-Item -ItemType Directory (Split-Path $cfgFile) -Force | Out-Null
  Set-Content -Path $cfgFile -Encoding ASCII -Value @('[Logging.Console]', 'Enabled = false')
}
Log 'BepInEx console switched off (Remote Play Together needs that).'

# ---- 4. old network mod out of the way -------------------------------------------------------------------------------
$plugins = Join-Path $GameDir 'BepInEx\plugins'
$disabled = Join-Path $GameDir 'BepInEx\plugins-disabled'
New-Item -ItemType Directory $plugins -Force | Out-Null
foreach ($n in 'DrgSurvivorCoopProbe.dll', 'LiteNetLib.dll') {
  $f = Join-Path $plugins $n
  if (Test-Path $f) {
    New-Item -ItemType Directory $disabled -Force | Out-Null
    Move-Item $f (Join-Path $disabled $n) -Force
    $state.movedOldMod += $n
    Log ('Old network mod moved aside: ' + $n)
  }
}

# ---- 5. the plugin ---------------------------------------------------------------------------------------------------
Copy-Item (Join-Path $payload 'LocalCoop.dll') (Join-Path $plugins 'LocalCoop.dll') -Force
Log 'LocalCoop.dll copied.'

# ---- 6. BepInEx off for a normal start -------------------------------------------------------------------------------
$ini = Join-Path $GameDir 'doorstop_config.ini'
if (-not (Test-Path $ini)) { throw 'doorstop_config.ini is missing: BepInEx is not installed correctly.' }
$lines = Get-Content $ini
$state.doorstopWasEnabled = -not ($lines | Where-Object { $_ -match '^\s*enabled\s*=\s*false' })
$lines = $lines | ForEach-Object { if ($_ -match '^\s*enabled\s*=') { 'enabled = false' } else { $_ } }
Set-Content -Path $ini -Value $lines -Encoding ASCII
Log 'BepInEx switched off for normal starts (doorstop_config.ini: enabled = false).'

# ---- 7. launcher, uninstaller, shortcut ------------------------------------------------------------------------------
# files of earlier versions of this package that no longer exist
foreach ($n in 'Start-Coop-RemotePlay.bat', 'Start-Coop-Direct.bat') { Remove-Item (Join-Path $GameDir $n) -ErrorAction SilentlyContinue }
foreach ($n in 'Start-Coop.bat', 'Uninstall-LocalCoop.ps1', 'Uninstall-LocalCoop.bat', 'RpLauncher.exe', 'Enable-RemotePlayTrick.ps1', 'Enable-RemotePlayTrick.bat', 'Enable-RemotePlayTrick-Undo.bat') {
  $src = Join-Path $payload $n
  if (Test-Path $src) { Copy-Item $src (Join-Path $GameDir $n) -Force }
}
$exe = Join-Path $GameDir 'DRG Survivor.exe'
$desk = if ($DesktopDir) { $DesktopDir } else { [Environment]::GetFolderPath('Desktop') }
function New-Shortcut($name, $target) {
  $lnk = Join-Path $desk ($name + '.lnk')
  $sh = New-Object -ComObject WScript.Shell
  $s = $sh.CreateShortcut($lnk)
  $s.TargetPath = $target
  $s.WorkingDirectory = $GameDir
  $s.IconLocation = $exe + ',0'
  $s.Save()
  $state.shortcuts += $lnk
}
if (-not $NoShortcuts) {
  try {
    New-Item -ItemType Directory $desk -Force | Out-Null
    New-Shortcut 'DRG Survivor Co-op' (Join-Path $GameDir 'Start-Coop.bat')
    Log 'Desktop shortcut created: DRG Survivor Co-op'
  } catch { Log ('Shortcut not created: ' + $_.Exception.Message) }
}

# ---- 8. Remote Play Together: the Spacewar trick ---------------------------------------------------------------------
# Steam offers "Remote Play Together" only for games that have that feature. Spacewar (app 480, Valve's sample app) has it. The trick puts a
# 5 KB launcher in place of Spacewar's exe: starting Spacewar from Steam then starts DRG Survivor with the co-op options.
$spw = ''
if ($SpacewarDir) { if (Test-Path $SpacewarDir) { $spw = (Resolve-Path $SpacewarDir).Path } }
else { $spw = Find-App '480' '' }
$remoteOn = $false
if (-not $hasRp) {
  Log 'Remote Play trick is not part of this package: skipped.'
} elseif ($spw) {
  try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $GameDir 'Enable-RemotePlayTrick.ps1') -DonorDir $spw -GameDir $GameDir | Out-Null
    if ($LASTEXITCODE -eq 0) { $state.spacewarDir = $spw; $remoteOn = $true; Log ('Remote Play Together enabled through Spacewar: ' + $spw) }
    else { Log ('Remote Play trick failed (exit code ' + $LASTEXITCODE + '). Run Enable-RemotePlayTrick.bat in the game folder to see why.') }
  } catch { Log ('Remote Play trick failed: ' + $_.Exception.Message) }
} else {
  Log 'Spacewar is not installed: Remote Play Together is not set up yet.'
  if (-not $Yes -and -not $SpacewarDir) {
    if (Ask 'Open the Steam install page of Spacewar (free, about 50 MB) now?') { try { Start-Process 'steam://install/480' } catch { } }
  }
}
if ($remoteOn -and -not $NoShortcuts) {
  try {
    $url = Join-Path $desk 'DRG Survivor Co-op (Remote Play).url'
    Set-Content -Path $url -Encoding ASCII -Value @('[InternetShortcut]', 'URL=steam://rungameid/480', ('IconFile=' + $exe), 'IconIndex=0')
    $state.shortcuts += $url
    Log 'Desktop shortcut created: DRG Survivor Co-op (Remote Play)'
  } catch { Log ('Remote Play shortcut not created: ' + $_.Exception.Message) }
}

($state | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $GameDir 'localcoop-install.json') -Encoding UTF8
Log 'Done.'

Say ''
Say 'Done. How to play:'
Say '  * Co-op (same PC, two controllers): desktop shortcut "DRG Survivor Co-op" (or Start-Coop.bat in the game folder)'
if ($remoteOn) {
  Say '  * Co-op with a friend online:       start "Spacewar" in Steam (shortcut "DRG Survivor Co-op (Remote Play)"), then Shift+Tab -> Friends -> Remote Play Together'
  Say '    In the Steam overlay (Shift+Tab) open the controller settings and change the profile to "Gamepad", or the game sees no controller.'
} elseif ($hasRp) {
  Say '  * Online with a friend:              install Spacewar (steam://install/480), then run Enable-RemotePlayTrick.bat in the game folder. See README.txt.'
}
Say '  * Single player:                    start the game normally from Steam; the mod is off then'
Say '  * The FIRST co-op start takes 1 to 3 minutes longer (BepInEx builds its files), after that it does not.'
if ($backup) { Say ('  * Save backup:                       ' + $backup) }
Say '  * Remove everything:                 Uninstall-LocalCoop.bat in the game folder'
