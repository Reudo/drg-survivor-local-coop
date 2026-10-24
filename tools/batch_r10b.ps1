# Round 10, part 2: HUD copy without the drone icon, hot rock damage for the second dwarf, top row of the main menu - each with its twin.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10b-hudcopy';E='-lcp1=clone-hud -lcquit=80'; D=$true},
 @{N='r10b-hudcopy-twin';E='-lcp1=clone-hud -lcquit=80 -lcdronehud'},
 @{N='r10b-hotrock';E='-lcui=start -lctest=start -lcquit=200 -lchotrocktest'},
 @{N='r10b-hotrock-twin';E='-lcui=start -lctest=start -lcquit=200 -lchotrocktest -lcnohotrockfix'},
 @{N='r10b-nav3';E='-lcmenunavtest -lcmenunavtest3 -lcnonavwatch -lcquit=25'; NA=$true},
 @{N='r10b-nav3-twin';E='-lcmenunavtest -lcmenunavtest3 -lcnonavwatch -lcnotoprowfix -lcquit=25'; NA=$true}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=200}
  if($r.D){ $a.Deploy=$true }
  if($r.NA){ $a.NoAuto=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|RESULT|navtest|top row|zones:|threw|error CS' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH10B DONE"
