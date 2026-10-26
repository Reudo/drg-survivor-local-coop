# Artifacts and the end screen (expected: each green run PASS/CHECK OK, each twin FAIL/CHECK FAIL).
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='art-endscreen';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcendtest'; D=$true},
 @{N='art-endscreen-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcendtest -lcshowartifacts'},
 @{N='art-levelprobe';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcbonusprobe'},
 @{N='art-sweep';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcartsweep'},
 @{N='art-sweep-twin';E='-lcgod -lcui=stage -lctest=stage -lcquit=60 -lcpoll -lctwopads -lcartsweep -lcnoartmirror'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  if ($r.D) { $a.Deploy = $true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'CHECK|SUMMARY|SWEEP|RESULT|endscreen2:|level artifact' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
