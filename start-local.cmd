@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo   Workslip - lokal udvikling
echo ========================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\dev\prepare-local-start.ps1"
set "PREP_EXIT=%ERRORLEVEL%"
if not "%PREP_EXIT%"=="0" (
  echo.
  echo [FEJL] Kunne ikke forberede Workslip lokalt.
  echo.
  pause
  exit /b %PREP_EXIT%
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" -Mobile
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
echo Computer: http://127.0.0.1:5270/app/overblik
echo Telefon-link vises ovenfor under "Phone".
echo.
exit /b 0
