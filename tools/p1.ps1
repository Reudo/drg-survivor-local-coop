# p1.ps1 <mode> [extra flags] - one Phase 1 experiment run, prints the experiment lines and the last player list.
param([Parameter(Mandatory)][string]$Mode, [string]$More = '', [int]$QuitAfter = 70, [switch]$Deploy, [switch]$NoGod)
$ErrorActionPreference = 'Continue'
$a = @{ Name = "p1-$Mode"; QuitAfter = $QuitAfter; Extra = ("-lcp1=$Mode " + $(if ($NoGod) { "" } else { "-lcgod " }) + $More); NoClick = $true }
if ($Deploy) { $a.Deploy = $true }
# The title screen of the game sometimes does not accept the start press (locked session); retry those starts only.
for ($try = 1; $try -le 3; $try++) {
  $lines = & (Join-Path $PSScriptRoot 'lc-run.ps1') @a 2>&1 | Select-String 'verdict|plugin:|restore|run:' | ForEach-Object { $_.Line }
  $lines | ForEach-Object { $_ }
  if (-not ($lines -match 'NEVER-REACHED-A-RUN')) { break }
  Write-Host "start did not get past the title screen (attempt $try of 3)"
  $a.Remove('Deploy')
}
$d = Get-ChildItem (Join-Path $PSScriptRoot '..\..\work\lc-runs') -Directory | Sort-Object Name | Select-Object -Last 1
Select-String -Path "$($d.FullName)\lc.log" -Pattern 'p1:' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(250, $_.Line.Length)) }
Get-Content "$($d.FullName)\lc-state.jsonl" | Select-String playerList | Select-Object -Last 1 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(450, $_.Line.Length)) }
