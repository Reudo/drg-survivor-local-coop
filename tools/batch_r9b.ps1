Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r9b-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'; D=$true},
 @{N='r9b-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'},
 @{N='r9b-levels';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst -lcarrowprobe'},
 @{N='r9b-perf';E='-lcui=start -lctest=start -lcquit=200 -lcperf -lctwopads'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK FAIL|RESULT|perf' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH9B DONE"
