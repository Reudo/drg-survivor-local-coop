# Regression subset after the artifact / weapon tick changes (expected: each green run PASS/CHECK OK, each twin FAIL/CHECK FAIL).
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='rg-artmirror-twin';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcbonusprobe -lcnoartmirror'},
 @{N='rg-support';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200'},
 @{N='rg-support-twin';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcnosupportmirror'},
 @{N='rg-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'},
 @{N='rg-pause-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopauseview'},
 @{N='rg-pause-nop2';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnop2pause'}
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
