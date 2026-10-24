$ErrorActionPreference='Continue'
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$first=$true
foreach($m in 'start','levelup','wallet','stage','target','input','anim','chooser','flowpad'){
  $extra = "-lcgod -lcui=$m -lctest=$m"
  $q = 200
  if($m -eq 'input' -or $m -eq 'chooser'){ $extra += ' -lcpoll' }
  if($m -eq 'flow' -or $m -eq 'flowpad'){ $extra = "-lcui=$m -lctest=$m"; $q = 40 }
  $a = @{ Name = "reg-$m"; Extra = $extra; QuitAfter = $q; MaxSeconds = 330 }
  if($first){ $a.Deploy = $true; $first=$false }
  try { & .\lc-run.ps1 @a 2>&1 | Select-String 'verdict|RESULT|restore' | ForEach-Object { "$m : $($_.Line)" } } catch { "$m : EXC $($_.Exception.Message)" }
}
"BATCH DONE"
