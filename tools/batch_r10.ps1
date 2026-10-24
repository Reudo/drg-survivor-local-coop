# Round 10: pause menu title, shop title, cross-pad confirm block, escort drill zone, HUD copy without drone icon, main menu top row, with twins.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10-pause';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest'; D=$true},
 @{N='r10-pause-twin-panel';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest -lcpausepanel'},
 @{N='r10-wallet';E='-lcgod -lcui=wallet -lctest=wallet -lcquit=200'},
 @{N='r10-wallet-twin';E='-lcgod -lcui=wallet -lctest=wallet -lcquit=200 -lcnoshoptitle'},
 @{N='r10-chooser';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'},
 @{N='r10-chooser-twin';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads -lcnocrossblock'},
 @{N='r10-hud';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnorevive'},
 @{N='r10-hud-twin';E='-lcui=retry -lctest=retry -lcquit=300 -lcpoll -lcnorevive -lcdronehud'},
 @{N='r10-nav3';E='-lcmenunavtest -lcmenunavtest3 -lcquit=25'; NA=$true},
 @{N='r10-nav3-twin';E='-lcmenunavtest -lcmenunavtest3 -lcnotoprowfix -lcquit=25'; NA=$true}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if($r.D){ $a.Deploy=$true }
  if($r.NA){ $a.NoAuto=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|hud|navtest|top row|chooser: shop|error CS' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH10 DONE"
