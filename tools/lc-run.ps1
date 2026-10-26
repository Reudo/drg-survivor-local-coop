<#
  lc-run.ps1 - one automated run of the local co-op prototype in the test copy (local\work\game-lc).

  What it does, in order: refuses to start while a game is running (never kills the owner's game),
  optionally builds and deploys the DLL (hash checked), backs up the save profile, launches the game,
  clicks the publisher splash (the only step that needs a real mouse click), waits for the plugin's own
  RESULT line, judges the screenshots, restores the saves and writes a run folder with everything.

  Usage:
    .\lc-run.ps1 -Deploy                        # boot test: menu -> run -> 40 s -> RESULT -> quit
    .\lc-run.ps1 -Extra '-lcshots=8'            # additional game flags
    .\lc-run.ps1 -Name 'baseline'               # run folder suffix

  A run is only as good as what it checked. PASS needs all of: plugin RESULT PASS, game exit by itself,
  at least one non-black screenshot in the run, saves restored byte-identical.
#>
param(
  [switch]$Deploy,
  [string]$Name = 'boot',
  [string]$Extra = '',
  [int]$QuitAfter = 40,
  [int]$ShotEvery = 10,
  [int]$MaxSeconds = 360,
  [switch]$NoAuto,
  [switch]$English,   # test only: the game itself runs in English (-lcgamelang=en), for screenshots
  [switch]$NoClick,  # do not click the splash from here (tests the in-game virtual gamepad alone)
  [switch]$KeepSaves # test only: leave the saves as the run left them (backup still made), for a check in the next launch; restore by hand from the backup
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'lc-paths.ps1')
$root = $LcRepo
if (-not (Test-Path $LcGame)) { throw "the test copy of the game is missing: $LcGame (create it with tools\new-test-copy.ps1, see SETUP.md)" }
$game = (Resolve-Path $LcGame).Path
$proj = Join-Path $root 'LocalCoop'

if (Get-Process 'DRG Survivor' -ErrorAction SilentlyContinue) {
  throw 'a DRG Survivor process is running - close it first (this script never kills a game)'
}

if ($Deploy) {
  Write-Host 'build: dotnet build -c Release'
  $out = & dotnet build (Join-Path $proj 'LocalCoop.csproj') -c Release --nologo "-p:GameDir=$game" 2>&1 | Out-String
  if ($out -notmatch '(?m)^\s+0 (Fehler|Error)') {
    ($out -split "`n") | Select-String ': error' | ForEach-Object { Write-Host $_.Line.Trim() }
    throw 'build failed'
  }
}
$src = Join-Path $proj 'bin\Release\LocalCoop.dll'
$dst = Join-Path $game 'BepInEx\plugins\LocalCoop.dll'
if (-not (Test-Path $src)) { throw "build output missing: $src (use -Deploy)" }
Copy-Item $src $dst -Force
if ((Get-FileHash $src).Hash -ne (Get-FileHash $dst).Hash) { throw 'DLL copy mismatch' }
Write-Host ('deployed LocalCoop.dll sha256={0}' -f (Get-FileHash $dst).Hash.Substring(0, 16))

# ---- saves: back up, restore, compare (same rule as the network prototype's harness) -------------
$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Funday Games\DRG Survivor\steam'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $LcRuns ("{0}-{1}" -f $stamp, $Name)
New-Item -ItemType Directory -Force $runDir | Out-Null
$backup = Join-Path $LcBackup ("lc-saves-{0}-{1}" -f $stamp, $Name)
New-Item -ItemType Directory -Force $backup | Out-Null
if (Test-Path $saveDir) { Copy-Item (Join-Path $saveDir '*') $backup -Recurse -Force }
Write-Host ('saves: backed up to {0}' -f $backup)

function Restore-Saves {
  if (-not (Test-Path $saveDir)) { return 'no-save-dir' }
  Get-ChildItem $saveDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  Copy-Item (Join-Path $backup '*') $saveDir -Recurse -Force
  $a = Get-ChildItem $backup -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName).Hash }
  $b = Get-ChildItem $saveDir -Recurse -File | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName).Hash }
  if (($a -join ',') -eq ($b -join ',')) { return 'identical' } else { return 'DIFFERENT' }
}

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class LcWin {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  delegate bool EnumProc(IntPtr h, IntPtr p);
  public static IntPtr GameWindow(uint want) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, p) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want || !IsWindowVisible(h)) return true;
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      if (c.ToString() == "UnityWndClass") { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  // Works without an active desktop (locked or disconnected session): the messages go to the game window's own queue.
  public static void PostPress(IntPtr h) {
    RECT c; GetClientRect(h, out c);
    int x = (c.Right - c.Left) / 2, y = (c.Bottom - c.Top) / 2;
    IntPtr lp = new IntPtr((y << 16) | (x & 0xFFFF));
    PostMessage(h, 0x0200, IntPtr.Zero, lp);                 // WM_MOUSEMOVE
    PostMessage(h, 0x0201, new IntPtr(1), lp);               // WM_LBUTTONDOWN
    System.Threading.Thread.Sleep(80);
    PostMessage(h, 0x0202, IntPtr.Zero, lp);                 // WM_LBUTTONUP
    PostMessage(h, 0x0100, new IntPtr(0x20), new IntPtr(1)); // WM_KEYDOWN space
    System.Threading.Thread.Sleep(80);
    PostMessage(h, 0x0101, new IntPtr(0x20), new IntPtr(unchecked((int)0xC0000001))); // WM_KEYUP space
  }
  public static void ClickCenter(IntPtr h) {
    ShowWindow(h, 9);
    // A bare SetForegroundWindow is refused while another window has the focus; a tapped ALT key and a
    // short topmost flip make the game window win, so the click lands on it and not on what is in front.
    keybd_event(0x12, 0, 0, IntPtr.Zero); keybd_event(0x12, 0, 2, IntPtr.Zero);
    SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
    SetForegroundWindow(h);
    RECT r; if (!GetWindowRect(h, out r)) return;
    System.Threading.Thread.Sleep(400);
    SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
  }
}
"@

$lcLog = Join-Path $game 'lc.log'
Remove-Item $lcLog, (Join-Path $game 'lc-state.jsonl') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $game 'lc-shots') -Recurse -Force -ErrorAction SilentlyContinue

$argList = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720')
if (-not $NoAuto) { $argList += '-lcauto' }
$argList += "-lcquit=$QuitAfter"
$argList += "-lcshots=$ShotEvery"
$argList += "-lctest=$Name"
if ($Extra) { $argList += ($Extra -split '\s+' | Where-Object { $_ }) }
if ($English) { $argList += '-lcgamelang=en' }
if (-not ($argList | Where-Object { $_ -like '-lcslot=*' })) { $argList += '-lcslot=3' }     # owner: tests always play in save slot 3 (slots 1 and 2 are the owner's); the saves are still backed up and restored
if (-not ($argList | Where-Object { $_ -like '-lclang=*' })) { $argList += '-lclang=en' }     # the checks read the English texts, whatever language the game is set to

$verdict = 'FAIL'
$notes = @()
$proc = $null
try {
  Write-Host ('launch: DRG Survivor.exe {0}' -f ($argList -join ' '))
  $proc = Start-Process -FilePath (Join-Path $game 'DRG Survivor.exe') -ArgumentList $argList -WorkingDirectory $game -PassThru
  $start = Get-Date
  $deadline = $start.AddSeconds($MaxSeconds)
  $passedSplash = $false

  while ((Get-Date) -lt $deadline) {
    $proc.Refresh()
    if ($proc.HasExited) { break }
    $log = if (Test-Path $lcLog) { Get-Content $lcLog -ErrorAction SilentlyContinue } else { @() }
    if ($log -match 'RESULT ') { break }
    if (-not $passedSplash) {
      if ($log -match 'phase: menu|auto: pressing|wizard page|IN RUN') { $passedSplash = $true; Write-Host 'splash: passed' }
      elseif ($log.Count -gt 0 -and -not $NoClick) {
        $h = [LcWin]::GameWindow([uint32]$proc.Id)
        if ($h -ne [IntPtr]::Zero) { [LcWin]::PostPress($h); [LcWin]::ClickCenter($h); Write-Host 'splash: click' }
      }
    }
    Start-Sleep -Seconds 3
  }

  # The plugin quits the game by itself after RESULT. Give it time; never kill it hard.
  $exitDeadline = (Get-Date).AddSeconds(45)
  while (-not $proc.HasExited -and (Get-Date) -lt $exitDeadline) { Start-Sleep -Seconds 1; $proc.Refresh() }
  if (-not $proc.HasExited) {
    $notes += 'game did not exit by itself after RESULT, closing the window'
    [void]$proc.CloseMainWindow()
    Start-Sleep -Seconds 15
    $proc.Refresh()
  }
  $exited = $proc.HasExited
  if (-not $exited) { $notes += 'GAME STILL RUNNING - left alone, close it yourself' }
}
finally {
  if ($proc -and $proc.HasExited) { Start-Sleep -Seconds 2 }
}

# ---- collect --------------------------------------------------------------------------------------
foreach ($f in 'lc.log', 'lc-state.jsonl') { if (Test-Path (Join-Path $game $f)) { Copy-Item (Join-Path $game $f) $runDir } }
$bepLog = Join-Path $game 'BepInEx\LogOutput.log'
if (Test-Path $bepLog) { Copy-Item $bepLog (Join-Path $runDir 'bepinex.log') }
if (Test-Path (Join-Path $game 'lc-shots')) { Copy-Item (Join-Path $game 'lc-shots') (Join-Path $runDir 'shots') -Recurse }

# ---- screenshots: judged here, not by the mod -------------------------------------------------------
Add-Type -AssemblyName System.Drawing
$shotReport = @()
$goodShots = 0
$shotDir = Join-Path $runDir 'shots'
if (Test-Path $shotDir) {
  foreach ($png in Get-ChildItem $shotDir -Filter *.png | Sort-Object Name) {
    try {
      $bmp = New-Object System.Drawing.Bitmap $png.FullName
      $small = New-Object System.Drawing.Bitmap 64, 36
      $g = [System.Drawing.Graphics]::FromImage($small); $g.DrawImage($bmp, 0, 0, 64, 36); $g.Dispose()
      $vals = New-Object System.Collections.Generic.List[double]
      for ($y = 0; $y -lt 36; $y++) { for ($x = 0; $x -lt 64; $x++) { $c = $small.GetPixel($x, $y); $vals.Add(0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B) } }
      $mean = ($vals | Measure-Object -Average).Average
      $sd = [Math]::Sqrt((($vals | ForEach-Object { ($_ - $mean) * ($_ - $mean) }) | Measure-Object -Average).Average)
      $ok = ($mean -gt 8) -and ($sd -gt 6)
      if ($ok) { $goodShots++ }
      $shotReport += ('{0} {1}x{2} mean={3:F0} sd={4:F0} {5}' -f $png.Name, $bmp.Width, $bmp.Height, $mean, $sd, $(if ($ok) { 'ok' } else { 'BLACK-OR-FLAT' }))
      $bmp.Dispose(); $small.Dispose()
    } catch { $shotReport += ('{0} unreadable: {1}' -f $png.Name, $_.Exception.Message) }
  }
}

$resultLine = if (Test-Path (Join-Path $runDir 'lc.log')) { (Select-String -Path (Join-Path $runDir 'lc.log') -Pattern 'RESULT ' | Select-Object -Last 1).Line } else { $null }
if ($KeepSaves) { $restore = 'kept'; $notes += "saves KEPT as the run left them (-KeepSaves), backup: $backup" } else { $restore = Restore-Saves }

$pluginPass = [bool]($resultLine -and $resultLine -match 'RESULT \S+ PASS')
$verdict = if ($pluginPass -and $exited -and $goodShots -ge 1 -and ($restore -eq 'identical' -or $restore -eq 'kept')) { 'PASS' } else { 'FAIL' }
$summary = @(
  "run: $runDir",
  "verdict: $verdict",
  "plugin: $resultLine",
  "game exited by itself: $exited",
  "screenshots: $($shotReport.Count) taken, $goodShots usable",
  "saves restore: $restore"
) + $shotReport + $notes
$summary | Set-Content (Join-Path $runDir 'result.txt')
$summary | ForEach-Object { Write-Host $_ }
if ($verdict -ne 'PASS') { exit 1 }
