Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$runs = @(
 @{N='r10c-bobby';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcmission1 -lcbobbytest'; D=$true},
 @{N='r10c-bobby-twin';E='-lcgod -lcui=start -lctest=start -lcquit=200 -lcmission1 -lcbobbytest -lcnobobbyzone'}
)
foreach($r in $runs){
  $a=@{Name=$r.N;Extra=$r.E;MaxSeconds=200}; if($r.D){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    Get-Content (Join-Path $d.FullName 'lc.log') | Select-String 'zones:|RESULT|threw|error CS' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
"BATCH10C DONE"
