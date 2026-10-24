Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r9c-perf';E='-lcui=start -lctest=start -lcquit=200 -lcperf -lctwopads'; D=$true},
 @{N='r9c-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'},
 @{N='r9c-pod';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest'},
 @{N='r9c-pages-de';E='-lcui=pages -lctest=pages -lcquit=60 -lclang=de'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK FAIL|RESULT|perf:' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH9C DONE"
