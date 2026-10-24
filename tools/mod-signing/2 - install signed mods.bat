@echo off
setlocal
rem Installs the signed builds from signed\ on this computer: the PC mod into the game, and the Android mod into
rem "android patch\mods" (then put it on the phone with "3 - put mod on phone.bat" there, as before).
rem Do this before or right after pushing to the server, so your own game has the same file the server hands out.
rem From 0.15.0 the builds are WFRevival.dll / WFRevival.Android.dll (the rename). For a WFRevival.dll you are asked
rem where to put it: the new place, or the old place under the old name - what a 0.14.x updater does - to test the
rem mod moving itself.
cd /d "%~dp0"
set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40k Warpforge"
set "PCNEW=%GAME%\Mods\WFRevival"
set "PCOLD=%GAME%\Mods\WarpforgeRevival"
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

rem (No paths inside ( ) blocks: the game's folder has "(x86)" in it, and its ")" would end the block early -
rem that is what skipped the Android copy before.)
if exist "signed\WFRevival.dll" goto check
if not exist "signed\WarpforgeRevival.dll" goto android
:check
tasklist /FI "IMAGENAME eq Warpforge.exe" 2>nul | find /I "Warpforge.exe" >nul
if errorlevel 1 goto pcpick
echo The game is running. Close it, then press a key to carry on.
pause >nul
goto check

:pcpick
if not exist "signed\WFRevival.dll" goto pcold
echo WFRevival.dll: where should it go?
echo   N = the new place, Mods\WFRevival\WFRevival.dll (a normal install)
echo   O = the old place under the old name, Mods\WarpforgeRevival\WarpforgeRevival.dll - like a 0.14.x
echo       updater does it - to test the mod moving itself
choice /C NO /M "New or Old place"
if errorlevel 2 goto pctestmove

rem ---- new place; the old copies are removed so only one loads
if not exist "%PCNEW%" mkdir "%PCNEW%"
if not exist "%PCNEW%\manifest.json" call :manifest
copy /y "signed\WFRevival.dll" "%PCNEW%\WFRevival.dll" >nul
if errorlevel 1 goto pcfailed
if exist "%PCOLD%\WarpforgeRevival.dll" del /q "%PCOLD%\WarpforgeRevival.dll"
if exist "%PCOLD%\manifest.json" del /q "%PCOLD%\manifest.json"
if exist "%PCOLD%" rmdir "%PCOLD%" 2>nul
if exist "%GAME%\Mods\WarpforgeRevival.dll" del /q "%GAME%\Mods\WarpforgeRevival.dll"
echo PC mod installed in "%PCNEW%"
goto android

:pctestmove
rem ---- old place, old name; any copy in the new place is removed so the move really happens
if not exist "%PCOLD%" mkdir "%PCOLD%"
copy /y "signed\WFRevival.dll" "%PCOLD%\WarpforgeRevival.dll" >nul
if errorlevel 1 goto pcfailed
if exist "%PCNEW%\WFRevival.dll" del /q "%PCNEW%\WFRevival.dll"
echo PC mod put in the OLD place: "%PCOLD%\WarpforgeRevival.dll"
echo Start the game: the mod copies itself to "%PCNEW%" and the old copy is removed when you close the game.
goto android

:pcold
if not exist "%PCOLD%" mkdir "%PCOLD%"
copy /y "signed\WarpforgeRevival.dll" "%PCOLD%\WarpforgeRevival.dll" >nul
if errorlevel 1 goto pcfailed
echo PC mod installed in "%PCOLD%"
goto android

:pcfailed
echo COULD NOT COPY the PC mod - try Run as administrator.

:android
if not exist "%ANDROID%" mkdir "%ANDROID%"
if exist "signed\WFRevival.Android.dll" goto androidnew
if not exist "signed\WarpforgeRevival.Android.dll" goto done
copy /y "signed\WarpforgeRevival.Android.dll" "%ANDROID%\WarpforgeRevival.Android.dll" >nul
if errorlevel 1 goto androidfailed
echo Android mod copied to "%ANDROID%"
goto done
:androidnew
copy /y "signed\WFRevival.Android.dll" "%ANDROID%\WFRevival.Android.dll" >nul
if errorlevel 1 goto androidfailed
rem the phone bat puts whichever is there on the phone: only the new one stays
if exist "%ANDROID%\WarpforgeRevival.Android.dll" del /q "%ANDROID%\WarpforgeRevival.Android.dll"
echo Android mod copied to "%ANDROID%" (WFRevival.Android.dll)
goto done
:androidfailed
echo COULD NOT COPY the Android mod to "%ANDROID%"

:done
echo.
pause
exit /b 0

:manifest
rem MelonLoader only loads mods from a folder inside Mods when that folder holds a manifest.json
> "%PCNEW%\manifest.json" echo {
>> "%PCNEW%\manifest.json" echo   "name": "WFRevival",
>> "%PCNEW%\manifest.json" echo   "description": "WF Revival mod. MelonLoader needs this file to load mods from this folder - do not delete it."
>> "%PCNEW%\manifest.json" echo }
exit /b 0
