Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r2-chooser';E='-lcui=chooser -lctest=chooser -lcquit=300 -lcpoll'},
 @{N='r2-stage';E='-lcgod -lcui=stage -lctest=stage -lcquit=200 -lcpoll -lctwopads'},
 @{N='r2-retry';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll'},
 @{N='r2-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'},
 @{N='r2-flowmouse';E='-lcui=flowmouse -lctest=flowmouse -lcquit=100'},
 @{N='r2-levelup';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200'},
 @{N='r2-wallet';E='-lcgod -lcui=wallet -lctest=wallet -lcquit=200'},
 @{N='r2-target';E='-lcgod -lcui=target -lctest=target -lcquit=200'},
 @{N='r2-dmg';E='-lcgod -lcui=dmg -lctest=dmg -lcquit=200'}
)
$first=$true
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=330}
  if($first){$a.Deploy=$true;$first=$false}
  try { & .\lc-run.ps1 @a 2>&1 | Select-String 'RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" } } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
