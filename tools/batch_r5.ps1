# Round 5: the checks of the polishing round (level-up order, headline, shared arrows, drop pod, early clone, menu pages) with their twins,
# then the whole round 4 batch as regression. Expected: each green run PASS / CHECK OK, each twin FAIL / CHECK FAIL.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$lv = '-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst -lcarrowprobe'
$runs = @(
 @{N='r5-levels';E=$lv},
 @{N='r5-levels-twin-alt';E="$lv -lcnoalternate"},
 @{N='r5-levels-twin-banner';E="$lv -lcnolevelbanner"},
 @{N='r5-arrows-shared';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcslotarrows'},
 @{N='r5-arrows-shared-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcslotarrows -lcnoarrowshared'},
 @{N='r5-pod';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest'},
 @{N='r5-pod-twin-ride';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest -lcnopodride'},
 @{N='r5-pod-twin-both';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest -lcnopodboth'},
 @{N='r5-pod-twin-guard';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest -lcnopodguard2'},
 @{N='r5-pod-twin-early';E='-lcui=start -lctest=start -lcquit=200 -lcpodtest -lcnoearlyclone'},
 @{N='r5-drone2';E='-lcui=start -lctest=start -lcquit=200 -lcdrone2 -lctwopads'},
 @{N='r5-pages';E='-lcui=pages -lctest=pages -lcquit=60'},
 @{N='r5-pages-twin';E='-lcui=pages -lctest=pages -lcquit=60 -lcnobackdrop'},
 @{N='r5-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'},
 @{N='r5-revive-twin-ring';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnorevivering'},
 @{N='r5-revive-twin-time';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcreviveseconds=5'},
 @{N='r5-hud';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnorevive'},
 @{N='r5-hud-twin-hud';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnorevive -lcnoearlyhud'},
 @{N='r5-hud-twin-clone';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnorevive -lcnoearlyclone'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|levelup banner|levelup arrows|drone2: t\+9' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
& .\batch_r4.ps1
"BATCH5 DONE"
