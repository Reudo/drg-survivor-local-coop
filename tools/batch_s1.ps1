Set-Location $PSScriptRoot
$e='-lclang=en -lcgamelang=en -lcdoc'
& .\lc-run.ps1 -Deploy -Name doc2-flow -Extra "-lcui=flowpad -lctest=flow $e -lcquit=40" -MaxSeconds 380 2>&1 | Out-Null
& .\lc-run.ps1 -Name doc2-arrow -Extra "-lcui=start -lctest=start $e -lcarrow2test -lcarrowpod -lcarrowshots -lcquit=100" -MaxSeconds 200 2>&1 | Out-Null
& .\lc-run.ps1 -Name doc2-levelup -Extra "-lcgod -lcui=levelup -lctest=levelup $e -lcquit=200" -MaxSeconds 330 2>&1 | Out-Null
& .\lc-run.ps1 -Name doc2-shop -Extra "-lcgod -lcui=wallet -lctest=wallet $e -lcquit=200" -MaxSeconds 330 2>&1 | Out-Null
"BATCH DONE"
