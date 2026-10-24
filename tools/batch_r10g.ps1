# Round 10, part 5: the chooser test (A and B held back for the other pad) with its twin, run twice.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10g-chooser-1';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'; D=$true},
 @{N='r10g-chooser-2';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads'},
 @{N='r10g-chooser-twin';E='-lcgod -lcui=chooser -lctest=chooser -lcquit=200 -lcpoll -lctwopads -lcnocrossblock'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=300}; if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'chooser-test: CHECK|RESULT' | ForEach-Object { $x=$_.Line -replace '\[[0-9.,]+\] ',''; $r.N + ' | ' + $x.Substring(0,[Math]::Min(200,$x.Length)) } | Select-String 'FAIL|RESULT'
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH10G DONE"
