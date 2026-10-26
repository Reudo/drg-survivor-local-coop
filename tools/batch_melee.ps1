# Melee guard (hits on a dwarf the enemy does not touch). Expected: the two green runs PASS, the twin FAIL; the revive test stays PASS.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='melee-a';E='-lcui=start -lctest=start -lcquit=200 -lcmeleetest'},
 @{N='melee-b';E='-lcui=start -lctest=start -lcquit=200 -lcmeleetest'},
 @{N='melee-twin';E='-lcui=start -lctest=start -lcquit=200 -lcmeleetest -lcnomeleeguard'},
 @{N='melee-noclear';E='-lcui=start -lctest=start -lcquit=200 -lcmeleetest -lcnomeleeclear'},
 @{N='melee-revive';E='-lcui=start -lctest=start -lcquit=200 -lcrevivetest'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=380}
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'melee: CHECK|revive: CHECK FAIL|revive: SUMMARY|RESULT|slot: loaded' | ForEach-Object { "$($r.N) : $($_.Line)" }
    "$($r.N) : " + ((Get-Content (Join-Path $d.FullName 'result.txt')) -match 'saves restore')
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
