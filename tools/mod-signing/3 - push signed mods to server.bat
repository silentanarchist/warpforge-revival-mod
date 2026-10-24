@echo off
setlocal
rem Publishes the signed builds in signed\ to the server (W:\mod). Every player's game offers the update at its next
rem start. Only builds signed with your key are copied.
rem From 0.15.0 the builds are WFRevival.dll / WFRevival.Android.dll (the rename): when one of those is in signed\,
rem it is pushed and the old-named file of that platform is removed from the server (the server serves either name,
rem the new one first). The download address players' games use has no file name in it, so 0.14.x games update too.
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
call :one WFRevival.dll WarpforgeRevival.dll
call :one WFRevival.Android.dll WarpforgeRevival.Android.dll
echo.
pause
exit /b 0

rem :one <new name> <old name> - push the new-named build if there is one (and drop the old-named file from the
rem server), otherwise the old-named build
:one
if exist "signed\%~1" goto onenew
if not exist "signed\%~2" exit /b 0
copy /y "signed\%~2" "%SERVER%\%~2.new" >nul && move /y "%SERVER%\%~2.new" "%SERVER%\%~2" >nul
if errorlevel 1 (echo COULD NOT PUSH %~2) else (echo Pushed %~2)
exit /b 0
:onenew
copy /y "signed\%~1" "%SERVER%\%~1.new" >nul && move /y "%SERVER%\%~1.new" "%SERVER%\%~1" >nul
if errorlevel 1 (
  echo COULD NOT PUSH %~1
  exit /b 0
)
echo Pushed %~1
if exist "%SERVER%\%~2" del /q "%SERVER%\%~2" && echo Removed the old-named %~2 from the server
exit /b 0
