# Round 8: settings file lives in BepInEx\config; an old file from the game folder is moved there, missing keys are added, values stay.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$gl = $LcGame
$new = Join-Path $gl 'BepInEx\config\localcoop-settings.txt'
$old = Join-Path $gl 'localcoop-settings.txt'
$keep = if (Test-Path $new) { Get-Content $new -Raw } else { $null }
Remove-Item $new -ErrorAction SilentlyContinue
Set-Content $old -Encoding ascii -Value "# made by an older version`r`nReviveMode = percent`r`nReviveHealthPercent = 33`r`nReviveHealthFlat = 20`r`n"
& .\lc-run.ps1 -Name 'r8-settings' -Extra '-lcui=start -lctest=start -lcquit=100 -lcgod' -MaxSeconds 300 -Deploy 2>&1 | Out-Null
$d = Get-ChildItem (Join-Path $gl '..\lc-runs') | Where-Object { $_.Name -like '*r8-settings' } | Sort-Object Name | Select-Object -Last 1
Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'settings:|RESULT' | ForEach-Object { "r8 : $($_.Line)" }
"r8 : old file still in the game folder = " + (Test-Path $old)
"r8 : config file content:"; Get-Content $new | ForEach-Object { "r8 :   $_" }
if ($keep -ne $null) { Set-Content $new -Encoding ascii -Value $keep } else { Remove-Item $new -ErrorAction SilentlyContinue }
"BATCH8 DONE"
