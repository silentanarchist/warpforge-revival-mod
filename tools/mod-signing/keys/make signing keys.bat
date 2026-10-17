@echo off
rem Makes the keys that sign Warpforge Revival mod builds. Run once.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "make-keys.ps1"
echo.
pause
