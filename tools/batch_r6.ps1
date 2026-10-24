# Round 6: gap checks (sector info numbers against the game's own counters, fog of war around player 2, support cards in both directions).
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r6-pages';E='-lcui=pages -lctest=pages -lcquit=60';D=$true},
 @{N='r6-pages-twin';E='-lcui=pages -lctest=pages -lcquit=60 -lcownsectorcount'},
 @{N='r6-minimap';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcminimap2test'},
 @{N='r6-minimap-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcminimap2test -lcnofogp2'},
 @{N='r6-support';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst'},
 @{N='r6-support-twin';E='-lcgod -lcui=levelup -lctest=levelup -lcquit=200 -lcxpburst -lcnosupportmirror'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|sector info|minimap2: fog|support card' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH6 DONE"
