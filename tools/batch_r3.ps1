Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r3-chooser';E='-lcui=chooser -lctest=chooser -lcquit=300 -lcpoll'},
 @{N='r3-chooser-twin';E='-lcui=chooser -lctest=chooser -lcquit=300 -lcpoll -lcnochooser'},
 @{N='r3-stage';E='-lcgod -lcui=stage -lctest=stage -lcquit=250 -lcpoll -lctwopads'},
 @{N='r3-retry';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll'},
 @{N='r3-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'},
 @{N='r3-flowmouse';E='-lcui=flowmouse -lctest=flowmouse -lcquit=100'},
 @{N='r3-levelup';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200'},
 @{N='r3-wallet';E='-lcgod -lcui=wallet -lctest=wallet -lcquit=200'},
 @{N='r3-target';E='-lcgod -lcui=target -lctest=target -lcquit=200'},
 @{N='r3-dmg';E='-lcgod -lcui=dmg -lctest=dmg -lcquit=200'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  try { & .\lc-run.ps1 @a 2>&1 | Select-String 'RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" } } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
