@echo off
setlocal
rem Installs the signed builds from signed\ on this computer: the PC mod into the game, and the Android mod into
rem "android patch\mods" (then put it on the phone with "3 - put mod on phone.bat" there, as before).
rem Do this before or right after pushing to the server, so your own game has the same file the server hands out.
cd /d "%~dp0"
set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40k Warpforge"
set "PCMOD=%GAME%\Mods\WarpforgeRevival"
set "ANDROID=%~dp0..\android patch\mods"

echo.
echo Checking the signatures...
powershell -NoProfile -ExecutionPolicy Bypass -File "sign-mod.ps1" -Check
if errorlevel 1 (
  echo Not installed: the files in signed\ are missing or not signed with your key.
  echo.
  pause
  exit /b 1
)
echo.

if not exist "signed\WarpforgeRevival.dll" goto android
:check
tasklist /FI "IMAGENAME eq Warpforge.exe" 2>nul | find /I "Warpforge.exe" >nul
if not errorlevel 1 (
  echo The game is running. Close it, then press a key to carry on.
  pause >nul
  goto check
)
if not exist "%PCMOD%" mkdir "%PCMOD%"
copy /y "signed\WarpforgeRevival.dll" "%PCMOD%\WarpforgeRevival.dll" >nul
if errorlevel 1 (echo COULD NOT COPY the PC mod to %PCMOD% - try Run as administrator.) else (echo PC mod installed in %PCMOD%)

:android
if exist "signed\WarpforgeRevival.Android.dll" (
  copy /y "signed\WarpforgeRevival.Android.dll" "%ANDROID%\WarpforgeRevival.Android.dll" >nul
  if errorlevel 1 (echo COULD NOT COPY the Android mod to %ANDROID%) else (echo Android mod copied to %ANDROID%)
)
echo.
pause
