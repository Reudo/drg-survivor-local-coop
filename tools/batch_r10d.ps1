# Round 10, part 3: main menu top row with the game's PLAY button hidden (the real trap), pause view per opening, B blocked for the other pad,
# damage numbers at player 2, explosions and revive weapons - each with its twin.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10d-nav';E='-lcmenunavtest -lchideplay -lcquit=25'; NA=$true; D=$true},
 @{N='r10d-nav-twin';E='-lcmenunavtest -lchideplay -lcnotoprowfix -lcquit=25'; NA=$true},
 @{N='r10d-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'},
 @{N='r10d-pause-twin-goals';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcnopausegoals'},
 @{N='r10d-pause-twin-sticky';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcstickyp2 -lcnopausevis'},
 @{N='r10d-chooser';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'},
 @{N='r10d-chooser-twin';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads -lcnocrossblock'},
 @{N='r10d-hud';E='-lcp1=clone-hud -lcquit=80'},
 @{N='r10d-hud-twin';E='-lcp1=clone-hud -lcquit=80 -lcnoclonedmgtext'},
 @{N='r10d-plant';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcplanttest'},
 @{N='r10d-plant-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcplanttest -lcnoplantfix'},
 @{N='r10d-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'},
 @{N='r10d-revive-twin';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnorefreeze'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=300}
  if($r.D){ $a.Deploy=$true }
  if($r.NA){ $a.NoAuto=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|navtest|top row|zones:|threw|error CS|hud patches' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH10D DONE"
