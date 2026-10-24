Set-Location $PSScriptRoot
& .\lc-run.ps1 -Deploy -Name r16-final -Extra '-lcgod -lcui=start -lctest=start -lcquit=200 -lctwopads -lcpausetest' -MaxSeconds 300 2>&1 | Out-Null
