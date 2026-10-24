Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
& .\lc-run.ps1 -Deploy -Name r9d-flowpad -Extra '-lcui=flowpad -lctest=flow -lcquit=40' -MaxSeconds 380 2>&1 | Select-String 'RESULT|CHECK FAIL' | ForEach-Object { "r9d-flowpad : $($_.Line)" }
$d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like '*r9d-flowpad' } | Sort-Object Name | Select-Object -Last 1
Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK FAIL|RESULT' | ForEach-Object { "r9d-flowpad : $($_.Line)" }
& .\batch_r5.ps1
"BATCH9D DONE"
