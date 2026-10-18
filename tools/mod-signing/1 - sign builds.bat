@echo off
rem Signs the builds in to-sign\ with your key; the signed copies go to signed\.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "sign-mod.ps1"
echo.
pause
