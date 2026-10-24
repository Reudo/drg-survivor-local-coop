<#
  build-installer.ps1 - builds the installer package for the owner's PC:

    local\release\DrgSurvivorLocalCoop-1.0.0-Installer.zip   (and the unpacked folder next to it)

  Contents: Install-LocalCoop.bat/.ps1, payload\ (LocalCoop.dll, the launchers, the uninstaller, bepinex-base (unpacked BepInEx) for PCs that have none),
  README.txt, LIES-MICH.txt (German), BUILDINFO.txt. No game files.

  Usage:  .\build-installer.ps1            # builds the plugin first
          .\build-installer.ps1 -NoBuild   # repack the last build
#>
param([switch]$NoBuild)
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
if (-not $bep) { throw "BepInEx be.789 zip not found in $LcDownloads (see SETUP.md for the download and its sha256)" }

if (-not (Test-Path (Join-Path $inst 'payload\RpLauncher.exe'))) { & (Join-Path $root 'rplauncher\build-rplauncher.ps1') }
$stageRoot = Join-Path $rel 'DrgSurvivorLocalCoop-Installer'
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory (Join-Path $stageRoot 'payload') -Force | Out-Null
Copy-Item (Join-Path $inst 'Install-LocalCoop.bat') $stageRoot
Copy-Item (Join-Path $inst 'Install-LocalCoop.ps1') $stageRoot
Copy-Item (Join-Path $inst 'README.txt') $stageRoot
Copy-Item (Join-Path $inst 'LIES-MICH.txt') $stageRoot
Copy-Item (Join-Path $root 'LICENSE') $stageRoot
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $stageRoot
Get-ChildItem (Join-Path $inst 'payload') -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $stageRoot 'payload') }
Copy-Item $dll (Join-Path $stageRoot 'payload\LocalCoop.dll')
# BepInEx goes in unpacked (payload\bepinex-base\): a ZIP inside the ZIP is flagged by mod sites such as Nexus Mods
Expand-Archive -Path $bep.FullName -DestinationPath (Join-Path $stageRoot 'payload\bepinex-base') -Force
$nested = @(Get-ChildItem $stageRoot -Recurse -File | Where-Object { $_.Extension -in '.zip', '.7z', '.rar' })
if ($nested.Count -gt 0) { throw ('the package would contain an archive inside the archive: ' + (($nested | ForEach-Object { $_.FullName.Substring($stageRoot.Length + 1) }) -join ', ')) }
$h = (Get-FileHash $dll -Algorithm SHA256).Hash
Set-Content -Path (Join-Path $stageRoot 'BUILDINFO.txt') -Encoding UTF8 -Value @(
  'Deep Rock Galactic: Survivor - Local Co-op 1.0.0',
  ('Built: ' + (Get-Date -Format 's')),
  ('LocalCoop.dll SHA-256: ' + $h),
  ('BepInEx (only for PCs without BepInEx): ' + $bep.Name))

$outZip = Join-Path $rel 'DrgSurvivorLocalCoop-1.0.0-Installer.zip'
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
