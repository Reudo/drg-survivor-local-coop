# Compiles RpLauncher.cs to installer\payload\RpLauncher.exe (about 5 KB) with the C# compiler that ships with Windows (.NET Framework 4).
# build-installer.ps1 runs this when the exe is missing; the exe is git-ignored.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
$out = Join-Path $repo 'installer\payload\RpLauncher.exe'
& $csc /nologo /target:exe /optimize+ "/out:$out" (Join-Path $PSScriptRoot 'RpLauncher.cs')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw 'compiling RpLauncher failed' }
Write-Host ("built {0} ({1} bytes)" -f $out, (Get-Item $out).Length)
