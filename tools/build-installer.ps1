<#
  build-installer.ps1 - builds the installer package for the owner's PC:

    local\release\DrgSurvivorLocalCoop-1.0.1-Installer.zip   (and the unpacked folder next to it)
    -Nexus: local\release\DrgSurvivorLocalCoop-1.0.1-Nexus.zip   (plugin + installer scripts + docs; BepInEx is a requirement, not included)

  Contents: Install-LocalCoop.bat/.ps1, payload\ (LocalCoop.dll, the launchers, the uninstaller, bepinex-base (unpacked BepInEx) for PCs that have none),
  README.txt, LIES-MICH.txt (German), BUILDINFO.txt. No game files.

  Usage:  .\build-installer.ps1            # builds the plugin first
          .\build-installer.ps1 -NoBuild   # repack the last build
#>
param([switch]$NoBuild, [switch]$Nexus)   # -Nexus: the reduced package for mod sites (no RpLauncher.exe, no Remote Play trick, no BepInEx)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$root = $LcRepo
$rel = $LcRelease
New-Item -ItemType Directory -Force $rel | Out-Null
$proj = Join-Path $root 'LocalCoop'
$inst = Join-Path $root 'installer'

if (-not $NoBuild) {
  Write-Host 'build: dotnet build -c Release'
  $out = & dotnet build (Join-Path $proj 'LocalCoop.csproj') -c Release --nologo "-p:GameDir=$LcGame" 2>&1 | Out-String
  if ($out -notmatch '0 Fehler|0 Error') { ($out -split "`n") | Select-String ': error' | ForEach-Object { Write-Host $_.Line.Trim() }; throw 'build failed' }
}
$dll = Join-Path $proj 'bin\Release\LocalCoop.dll'
if (-not (Test-Path $dll)) { throw "build output missing: $dll" }
$bep = Get-ChildItem $LcDownloads -ErrorAction SilentlyContinue -Filter 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789*.zip' | Select-Object -First 1
if (-not $bep -and -not $Nexus) { throw "BepInEx be.789 zip not found in $LcDownloads (see SETUP.md for the download and its sha256)" }

if (-not $Nexus -and -not (Test-Path (Join-Path $inst 'payload\RpLauncher.exe'))) { & (Join-Path $root 'rplauncher\build-rplauncher.ps1') }
$stageRoot = Join-Path $rel $(if ($Nexus) { 'DrgSurvivorLocalCoop-Nexus' } else { 'DrgSurvivorLocalCoop-Installer' })
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory (Join-Path $stageRoot 'payload') -Force | Out-Null
Copy-Item (Join-Path $inst 'Install-LocalCoop.bat') $stageRoot
Copy-Item (Join-Path $inst 'Install-LocalCoop.ps1') $stageRoot
Copy-Item (Join-Path $root 'LICENSE') $stageRoot
$rpFiles = 'RpLauncher.exe', 'Enable-RemotePlayTrick.ps1', 'Enable-RemotePlayTrick.bat', 'Enable-RemotePlayTrick-Undo.bat'
Get-ChildItem (Join-Path $inst 'payload') -File | Where-Object { -not $Nexus -or $rpFiles -notcontains $_.Name } | ForEach-Object { Copy-Item $_.FullName (Join-Path $stageRoot 'payload') }
Copy-Item $dll (Join-Path $stageRoot 'payload\LocalCoop.dll')
if (-not $Nexus) {
  Copy-Item (Join-Path $inst 'README.txt') $stageRoot
  Copy-Item (Join-Path $inst 'LIES-MICH.txt') $stageRoot
  Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $stageRoot
  # BepInEx goes in unpacked (payload\bepinex-base\): a ZIP inside the ZIP is flagged by mod sites such as Nexus Mods
  Expand-Archive -Path $bep.FullName -DestinationPath (Join-Path $stageRoot 'payload\bepinex-base') -Force
} else {
  # README: the full one without the Remote Play (Spacewar trick) parts, with a requirements block on top
  $src = @(Get-Content (Join-Path $inst 'README.txt'))
  $secs = New-Object System.Collections.ArrayList; $cur = $null
  for ($i = 0; $i -lt $src.Count; $i++) {
    if ($i + 1 -lt $src.Count -and $src[$i].Trim() -ne '' -and $src[$i + 1] -match '^-{3,}\s*$') { $cur = @{ title = $src[$i]; lines = New-Object System.Collections.ArrayList }; [void]$secs.Add($cur) }
    if ($cur) { [void]$cur.lines.Add($src[$i]) }
  }
  $rd = New-Object System.Collections.ArrayList
  [void]$rd.AddRange(@('DEEP ROCK GALACTIC: SURVIVOR - LOCAL CO-OP', '==========================================', '',
    'Requirements', '------------',
    '- Deep Rock Galactic: Survivor (Steam, Windows), two controllers for two players on one PC',
    '- BepInEx 6.0.0-be.789 (Unity IL2CPP, win-x64), NOT included: https://builds.bepinex.dev/projects/bepinex_be',
    '  Unpack it into the game folder, start the game once, close it. Then run the installer below.',
    '  (The full package on GitHub, https://github.com/Reudo/drg-survivor-local-coop/releases, includes BepInEx and the optional',
    '  Steam Remote Play Together helper for playing with a friend online.)', ''))
  foreach ($sec in $secs) {
    if ($sec.title -like 'Remote Play Together*') { continue }
    $skip = $false
    foreach ($ln in $sec.lines) {
      if ($ln -match 'installs BepInEx only|sets up Remote Play Together|^\s+if not, it offers to open') { $skip = ($ln -match 'sets up Remote Play'); continue }
      if ($skip -and $ln -match '^\s+if not') { $skip = $false; continue }
      if ($ln -match '^- Co-op online with a friend') { $skip = $true; continue }
      if ($skip -and $ln -match '^\s{10,}\S') { continue }
      $skip = $false
      [void]$rd.Add($ln)
    }
  }
  $rdText = ($rd -join "`r`n")
  $rdText = [regex]::Replace($rdText, "the Remote Play trick \(Spacewar's exe\s+is restored\), ", '')
  $rdText = [regex]::Replace($rdText, " \(a second game window blocks the guest's input in Remote Play Together\)", '')
  Set-Content -Path (Join-Path $stageRoot 'README.txt') -Value $rdText -Encoding UTF8
  $tp = Get-Content (Join-Path $root 'THIRD-PARTY-NOTICES.txt') -Raw
  $tp = [regex]::Replace($tp, '(?m)^The release ZIP contains an unchanged copy of BepInEx[^\r\n]*\r?\n[^\r\n]*\r?\n', 'This package does not contain BepInEx. It needs BepInEx 6.0.0-be.789 (Unity IL2CPP, win-x64), installed separately; BepInEx is not part of this project''s MIT license.' + "`r`n")
  Set-Content -Path (Join-Path $stageRoot 'THIRD-PARTY-NOTICES.txt') -Value $tp -Encoding UTF8
}
$nested = @(Get-ChildItem $stageRoot -Recurse -File | Where-Object { $_.Extension -in '.zip', '.7z', '.rar' })
if ($nested.Count -gt 0) { throw ('the package would contain an archive inside the archive: ' + (($nested | ForEach-Object { $_.FullName.Substring($stageRoot.Length + 1) }) -join ', ')) }
$h = (Get-FileHash $dll -Algorithm SHA256).Hash
Set-Content -Path (Join-Path $stageRoot 'BUILDINFO.txt') -Encoding UTF8 -Value @(
  'Deep Rock Galactic: Survivor - Local Co-op 1.0.1',
  ('Built: ' + (Get-Date -Format 's')),
  ('LocalCoop.dll SHA-256: ' + $h),
  $(if ($Nexus) { 'BepInEx: not included (requirement: 6.0.0-be.789, Unity IL2CPP, win-x64)' } else { 'BepInEx (only for PCs without BepInEx): ' + $bep.Name }))

$outZip = Join-Path $rel $(if ($Nexus) { 'DrgSurvivorLocalCoop-1.0.1-Nexus.zip' } else { 'DrgSurvivorLocalCoop-1.0.1-Installer.zip' })
if (Test-Path $outZip) { Remove-Item $outZip -Force }
# entries with forward slashes (Compress-Archive of Windows PowerShell 5.1 writes backslashes, which other unpackers turn into file names)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($outZip, 'Create')
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
Get-ChildItem $stageRoot -Recurse -File | ForEach-Object {
  $name = $_.FullName.Substring($stageRoot.Length + 1).Replace([string][char]92, '/')
  [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal)
}
$zip.Dispose(); $fs.Dispose()
Write-Host ('zip: ' + $outZip + '  (' + [math]::Round((Get-Item $outZip).Length / 1MB, 1) + ' MB)')
Write-Host ('plugin sha256: ' + $h)
