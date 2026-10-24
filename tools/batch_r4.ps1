# Round 4 regression: every new check with its twin (expected: each green run PASS/CHECK OK, each twin FAIL/CHECK FAIL).
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r4-padpick';E='-lcui=padpick -lctest=padpick -lcquit=100'},
 @{N='r4-padpick-twin';E='-lcui=padpick -lctest=padpick -lcquit=100 -lcnofocusfix'},
 @{N='r4-artmirror';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcbonusprobe'},
 @{N='r4-artmirror-twin';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcbonusprobe -lcnoartmirror'},
 @{N='r4-support';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200'},
 @{N='r4-support-twin';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcnosupportmirror'},
 @{N='r4-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'},
 @{N='r4-revive-twin';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnorevive'},
 @{N='r4-crate';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lccratetest'},
 @{N='r4-crate-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lccratetest -lcnocrateshare'},
 @{N='r4-arrows';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcslotarrows'},
 @{N='r4-arrows-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcslotarrows -lcnoslotredirect'},
 @{N='r4-minimap';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcminimap2test'},
 @{N='r4-minimap-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcminimap2test -lcnominimap2'},
 @{N='r4-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'},
 @{N='r4-pause-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopauseview'},
 @{N='r4-pause-nop2';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnop2pause'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
