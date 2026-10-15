@echo off
setlocal
cd /d "%~dp0"

echo [workslip] Starting local Docker stack...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\demo.ps1" up
if errorlevel 1 (
  echo.
  echo [workslip] Startup failed. Review the output above.
  pause
  exit /b 1
)

start "" "http://127.0.0.1:5270/app/overblik"
echo.
echo [workslip] Workslip is running locally.
pause
