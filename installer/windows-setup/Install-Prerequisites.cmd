@echo off
rem Double-click wrapper for Install-Prerequisites.ps1 (any arguments are forwarded).
chcp 65001 >nul
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Prerequisites.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
echo.
pause
exit /b %EXITCODE%
