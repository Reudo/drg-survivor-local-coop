# Paths of the local co-op project, shared by all scripts in this folder. Dot-source it:   . (Join-Path $PSScriptRoot 'lc-paths.ps1')
#
#   $LcRepo     the repository root (this folder's parent)
#   $LcWork     work area, git-ignored: the test copy of the game, test runs, save backups, release output
#               default: <repo>\work, or the environment variable LC_WORK
#   $LcGame     the test copy of the game (never your Steam install)   <work>\game-lc
#   $LcRuns     one folder per test run                                 <work>\lc-runs
#   $LcBackup   save backups made before every test run                 <work>\backup
#   $LcRelease  the installer package                                   <work>\release
#   $LcDownloads  where build-installer.ps1 looks for the BepInEx zip (see SETUP.md)   <work>\downloads
#
# To keep the work area somewhere else (another drive, an older checkout), create tools\lc-local.ps1 (git-ignored) and set any of these variables in it.
$LcRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$LcOverride = Join-Path $PSScriptRoot 'lc-local.ps1'
if (Test-Path $LcOverride) { . $LcOverride }
if (-not $LcWork) { $LcWork = if ($env:LC_WORK) { $env:LC_WORK } else { Join-Path $LcRepo 'work' } }
if (-not $LcGame) { $LcGame = Join-Path $LcWork 'game-lc' }
if (-not $LcRuns) { $LcRuns = Join-Path $LcWork 'lc-runs' }
if (-not $LcBackup) { $LcBackup = Join-Path $LcWork 'backup' }
if (-not $LcRelease) { $LcRelease = Join-Path $LcWork 'release' }
if (-not $LcDownloads) { $LcDownloads = Join-Path $LcWork 'downloads' }
