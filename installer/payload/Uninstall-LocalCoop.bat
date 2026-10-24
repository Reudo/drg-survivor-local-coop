@echo off
rem Removes the local co-op mod. After a successful run it deletes this file and the script, nothing is left behind.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-LocalCoop.ps1" %*
if errorlevel 1 (
  echo.
  pause
  exit /b 1
)
del "%~dp0Uninstall-LocalCoop.ps1" >nul 2>&1
echo.
pause
(goto) 2>nul & del "%~f0"
