param([string]$Commit)
Set-Location (Join-Path $PSScriptRoot '..')
$wt = Join-Path (Split-Path (Get-Location) -Parent) 'wt-bis'
if (Test-Path $wt) { git worktree remove --force $wt 2>&1 | Out-Null }
git worktree add -q $wt $Commit 2>&1 | Out-Null
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$game = $LcGame
& dotnet build (Join-Path $wt 'LocalCoop\LocalCoop.csproj') -c Release --nologo "-p:GameDir=$game" 2>&1 | Out-Null
Copy-Item (Join-Path $wt 'LocalCoop\bin\Release\LocalCoop.dll') (Join-Path $game 'BepInEx\plugins\LocalCoop.dll') -Force
& (Join-Path $PSScriptRoot 'lc-run.ps1') -Name ("bis-" + $Commit) -Extra '-lcgod -lcui=mouse -lcquit=60' -MaxSeconds 200 2>&1 | Out-Null
git worktree remove --force $wt 2>&1 | Out-Null
