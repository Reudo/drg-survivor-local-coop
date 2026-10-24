Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r9e-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'; D=$true},
 @{N='r9e-padpick';E='-lcui=padpick -lctest=padpick -lcquit=100'},
 @{N='r9e-padpick-twin';E='-lcui=padpick -lctest=padpick -lcquit=100 -lcnofocusfix'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK FAIL|RESULT|player 2 ready|padpick: CHECK' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH9E DONE"
