# Round 9: the checks of the hand-test round (menu focus after BACK, German without flicker, sector goal numbers, pad hints B / menu button,
# blue XP cubes, pause menu owner/stats/class/pause level, red player 1, pod needs both, downed dwarf lies still) with their twins.
# Expected: each green run PASS / CHECK OK, each twin FAIL / CHECK FAIL.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r9-pages';E='-lcui=pages -lctest=pages -lcquit=60'; D=$true},
 @{N='r9-pages-twin-sel';E='-lcui=pages -lctest=pages -lcquit=60 -lcnoselrestore'},
 @{N='r9-pages-twin-sector';E='-lcui=pages -lctest=pages -lcquit=60 -lcgamesectorcount'},
 @{N='r9-pages-de';E='-lcui=pages -lctest=pages -lcquit=60 -lclang=de'},
 @{N='r9-pages-de-twin';E='-lcui=pages -lctest=pages -lcquit=60 -lclang=de -lcnoinstantloc'},
 @{N='r9-flowpad';E='-lcui=flowpad -lctest=flow -lcquit=40'},
 @{N='r9-flowpad-twin';E='-lcui=flowpad -lctest=flow -lcquit=40 -lcpadfocusall'},
 @{N='r9-flow';E='-lcui=flow -lctest=flow -lcquit=40'},
 @{N='r9-cubes';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lccubetest'},
 @{N='r9-cubes-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lccubetest -lcnocubes'},
 @{N='r9-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'},
 @{N='r9-pause-twin-stats';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopausestats'},
 @{N='r9-pause-twin-fix';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopausefix'},
 @{N='r9-pause-twin-owner';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopauseowner'},
 @{N='r9-levels';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst -lcarrowprobe'},
 @{N='r9-levels-twin-red';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst -lcarrowprobe -lcorangep1'},
 @{N='r9-pod';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest'},
 @{N='r9-pod-twin-both';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest -lcnopodboth'},
 @{N='r9-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'},
 @{N='r9-revive-twin-freeze';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnofreeze'},
 @{N='r9-revive-twin-retarget';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnoretargetdown'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|ui: sector info shown|selection restored|XP cubes|pod: the pod tests|lies still|pose:' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH9 DONE"
