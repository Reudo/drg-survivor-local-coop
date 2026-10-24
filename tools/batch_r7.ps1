# Round 7: Debug / DebugGodMode in localcoop-settings.txt. Runs write the settings file of the TEST copy, the default file is put back at the end.
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$set = Join-Path $LcGame 'BepInEx\config\localcoop-settings.txt'
$orig = if (Test-Path $set) { Get-Content $set -Raw } else { $null }
function W($dbg,$god){ Set-Content $set -Encoding ascii -Value ("ReviveSeconds = 3`r`nDebug = $dbg`r`nDebugGodMode = $god`r`n") }
$runs = @(
 @{N='r7-debug-god';D='on';G='on';Deploy=$true},
 @{N='r7-debug-nogod';D='on';G='off'},
 @{N='r7-nodebug-god';D='off';G='on'},
 @{N='r7-off';D='off';G='off'}
)
foreach($r in $runs){
  W $r.D $r.G
  $a=@{Name=$r.N;Extra='-lcui=start -lctest=start -lcquit=200 -lcgodtest';MaxSeconds=380}
  if($r.Deploy){ $a.Deploy=$true }
  try {
    & .\lc-run.ps1 @a 2>&1 | Out-Null
    $d = Get-ChildItem $LcRuns | Where-Object { $_.Name -like ('*' + $r.N) } | Sort-Object Name | Select-Object -Last 1
    $log = Get-Content (Join-Path $d.FullName 'lc.log')
    "$($r.N) : settings lines: " + (($log | Select-String 'settings: (DEBUG|DebugGod)' | ForEach-Object { $_.Line }) -join ' | ')
    "$($r.N) : trace lines (lvltrace/arrow/pickupwatch) = " + ($log | Select-String 'lvltrace|^.* arrow: |pickupwatch:' ).Count
    $log | Select-String 'god: CHECK' | ForEach-Object { "$($r.N) : $($_.Line)" }
    "$($r.N) : vitals = " + (($log | Select-String 'p1: vitals' | Select-Object -First 3 | ForEach-Object { $_.Line.Substring(0,[Math]::Min(150,$_.Line.Length)) }) -join ' || ')
    $log | Select-String 'RESULT' | ForEach-Object { "$($r.N) : $($_.Line)" }
  } catch { "$($r.N) : EXC $($_.Exception.Message)" }
}
if ($orig -ne $null) { Set-Content $set -Encoding ascii -Value $orig } else { Remove-Item $set -ErrorAction SilentlyContinue }
"BATCH7 DONE"
