@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo   Workslip - lokal udvikling
echo ========================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo.
  echo [FEJL] Workslip kunne ikke startes lokalt.
  echo Se fejlbeskeden ovenfor. Vinduet holdes aabent, saa den kan kopieres.
  echo.
  pause
  exit /b %EXIT_CODE%
)

echo.
echo [OK] Workslip er startet lokalt.
echo Overblik: http://127.0.0.1:5270/app/overblik
echo.
exit /b 0
