Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='fin-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'},
 @{N='fin-flow';E='-lcui=flow -lctest=flow -lcquit=40'},
 @{N='fin-chooser';E='-lcui=chooser -lctest=chooser -lcquit=300 -lcpoll'},
 @{N='fin-retry';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcperf'},
 @{N='fin-retry-nodead';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnodeadplayer'},
 @{N='fin-retry-noloadwait';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnodeadplayer -lcnoloadwait'}
)
$first=$true
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=330}
  if($first){$a.Deploy=$true;$first=$false}
  try { & .\lc-run.ps1 @a 2>&1 | Select-String 'verdict|RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" } } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
