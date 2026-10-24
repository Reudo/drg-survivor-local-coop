# Round 10, part 4: main menu top row with the game's PLAY button hidden (the real trap), pause view per opening, B blocked for the other pad,
# damage numbers at player 2, explosions and revive weapons - each with its twin.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10e-nav';E='-lcmenunavtest -lchideplay -lcquit=25'; NA=$true; D=$true},
 @{N='r10e-nav-twin';E='-lcmenunavtest -lchideplay -lcnotoprowfix -lcquit=25'; NA=$true},
 @{N='r10e-chooser';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'},
 @{N='r10e-chooser-twin';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads -lcnocrossblock'},
 @{N='r10e-hud';E='-lcp1=clone-hud -lcquit=80'},
 @{N='r10e-hud-twin';E='-lcp1=clone-hud -lcquit=80 -lcnoclonedmgtext'},
 @{N='r10e-plant';E='-lcui=start -lctest=start -lcquit=200 -lcplanttest'},
 @{N='r10e-plant-twin';E='-lcui=start -lctest=start -lcquit=200 -lcplanttest -lcnoplantfix'},
 @{N='r10e-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'},
 @{N='r10e-revive-twin';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest -lcnorefreeze'}
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
"BATCH10E DONE"
