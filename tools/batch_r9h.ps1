Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r9h-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'; D=$true},
 @{N='r9h-padpick';E='-lcui=padpick -lctest=padpick -lcquit=100'},
 @{N='r9h-flowmouse';E='-lcui=flowmouse -lctest=flowmouse -lcquit=100'},
 @{N='r9h-chooser';E='-lcui=chooser -lctest=chooser -lcquit=300 -lcpoll'},
 @{N='r9h-retry';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll'},
 @{N='r9h-wallet';E='-lcgod -lcui=wallet -lctest=wallet -lcquit=200'},
 @{N='r9h-target';E='-lcgod -lcui=target -lctest=target -lcquit=200'},
 @{N='r9h-dmg';E='-lcgod -lcui=dmg -lctest=dmg -lcquit=200'},
 @{N='r9h-stage';E='-lcgod -lcui=stage -lctest=stage -lcquit=250 -lcpoll -lctwopads'},
 @{N='r9h-cubes';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lccubetest'},
 @{N='r9h-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK FAIL|RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH9H DONE"
