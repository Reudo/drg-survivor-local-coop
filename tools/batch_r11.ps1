# Round 11: B must not open the pause menu on its own (with twin), plus the pause and chooser checks as regression.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r11-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'; D=$true},
 @{N='r11-pause-twin-backpause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcbackpause'},
 @{N='r11-chooser';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=300}; if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'pauseview: (CHECK|SUMMARY)|chooser-test: CHECK|RESULT|error CS' | ForEach-Object { $x=$_.Line -replace '\[[0-9.,]+\] ',''; $r.N + ' | ' + $x.Substring(0,[Math]::Min(220,$x.Length)) } | Select-String 'FAIL|SUMMARY|RESULT|B alone'
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH11 DONE"
