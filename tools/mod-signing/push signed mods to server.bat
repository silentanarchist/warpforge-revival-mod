@echo off
setlocal
rem Publishes the signed builds in signed\ to the server (W:\mod). Every player's game offers the update at its next
rem start. Only builds signed with your key are copied.
cd /d "%~dp0"
set "SERVER=W:\mod"

echo.
if not exist "%SERVER%\" (
  echo %SERVER% is not reachable - is the W: drive connected?
  echo.
  pause
  exit /b 1
)
echo Checking the signatures...
powershell -NoProfile -ExecutionPolicy Bypass -File "sign-mod.ps1" -Check
if errorlevel 1 (
  echo Nothing pushed: the files in signed\ are missing or not signed with your key.
  echo.
  pause
  exit /b 1
)
echo.
echo This sends the builds above to the server; players get them at their next game start.
choice /C YN /M "Push them now"
if errorlevel 2 (
  echo Nothing pushed.
  echo.
  pause
  exit /b 0
)
for %%F in (WarpforgeRevival.dll WarpforgeRevival.Android.dll) do (
  if exist "signed\%%F" (
    copy /y "signed\%%F" "%SERVER%\%%F.new" >nul && move /y "%SERVER%\%%F.new" "%SERVER%\%%F" >nul
    if errorlevel 1 (echo COULD NOT PUSH %%F) else (echo Pushed %%F)
  )
)
echo.
pause
