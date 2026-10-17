@echo off
setlocal
cd /d "%~dp0"
title Warpforge for Android - put the mod on the phone

echo.
echo  Warpforge for Android - put the mod on the phone
echo  -----------------------------------------------------
echo.
if not exist "mods\WarpforgeRevival.Android.dll" (
    echo  The mod ^(WarpforgeRevival.Android.dll^) is missing from the "mods" folder.
    goto :end
)

set "ADB="
if exist "tool\platform-tools\adb.exe" set "ADB=%~dp0tool\platform-tools\adb.exe"
if not defined ADB for /f "delims=" %%A in ('where adb 2^>nul') do if not defined ADB set "ADB=%%A"
if not defined ADB (
    echo  This needs Google's "platform-tools" ^(the adb program^).
    echo  Download "SDK Platform-Tools for Windows" from
    echo    https://developer.android.com/tools/releases/platform-tools
    echo  and unzip it so that this file exists:
    echo    %~dp0tool\platform-tools\adb.exe
    echo  Then run this again. ^(Or install from the phone itself - see READ ME.txt.^)
    goto :end
)

echo  Phone connected by USB, with "USB debugging" switched on?
echo  Devices found:
"%ADB%" devices
echo.

rem ---- choose the phone: the first real device that is ready (emulators only if nothing else) ----
setlocal enabledelayedexpansion
set "DEV="
for /f "skip=1 tokens=1,2" %%A in ('""%ADB%" devices"') do (
    set "N=%%A"
    if "%%B"=="device" if /i not "!N:~0,9!"=="emulator-" if not defined DEV set "DEV=%%A"
)
if not defined DEV for /f "skip=1 tokens=1,2" %%A in ('""%ADB%" devices"') do if "%%B"=="device" if not defined DEV set "DEV=%%A"
if not defined DEV (
    echo  No phone is ready. Check the cable, that USB debugging is on, and that the
    echo  "Allow USB debugging?" prompt on the phone was accepted. A phone listed as
    echo  "unauthorized" or "offline" is not ready yet.
    goto :end
)
echo  Using device: !DEV!
echo.
set "P=/sdcard/MelonLoader/com.Everguild.WarhammerWarpforge"
"%ADB%" -s !DEV! shell mkdir -p %P%/Mods
rem the earlier test build must not load next to the real mod: it is moved aside, not deleted
"%ADB%" -s !DEV! shell "mkdir -p %P%/Mods-disabled; if [ -f %P%/Mods/WarpforgeRevival.AndroidTest.dll ]; then mv %P%/Mods/WarpforgeRevival.AndroidTest.dll %P%/Mods-disabled/; fi"
rem the retired OpenGL graphics plugin is moved aside too (the game then draws the way it chooses itself)
"%ADB%" -s !DEV! shell "mkdir -p %P%/Plugins-disabled; if [ -f %P%/Plugins/WarpforgeRevival.AndroidGraphics.dll ]; then mv %P%/Plugins/WarpforgeRevival.AndroidGraphics.dll %P%/Plugins-disabled/; fi"
"%ADB%" -s !DEV! push "mods\WarpforgeRevival.Android.dll" %P%/Mods/WarpforgeRevival.Android.dll
if errorlevel 1 (
    echo.
    echo  Copying failed. Has the patched game been started at least once on this phone?
    goto :end
)
echo.
echo  The mod is on the phone. Mods folder now holds:
"%ADB%" -s !DEV! shell ls -la %P%/Mods
echo.
echo  Next: run "0 - get phone logs.bat" and start the game when it asks.

:end
echo.
pause
