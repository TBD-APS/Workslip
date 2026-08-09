@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-local-db.ps1" %*
exit /b %ERRORLEVEL%
