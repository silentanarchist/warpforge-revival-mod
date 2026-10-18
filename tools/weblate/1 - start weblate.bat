@echo off
rem Starts Weblate (and its database and cache) in Docker. Docker Desktop has to be running.
rem The first start downloads the images and sets everything up: give it a few minutes.
cd /d "%~dp0"
if not exist weblate.env copy weblate.env.example weblate.env >nul
rem the network Caddy and Weblate share (already there after the first time)
docker network inspect wf-proxy >nul 2>&1 || docker network create wf-proxy >nul
findstr /c:"CHANGE-ME" weblate.env >nul
if not errorlevel 1 goto unfilled
docker compose up -d
if errorlevel 1 goto failed
echo.
echo Weblate is starting. In a minute or two it answers on http://127.0.0.1:8090 on this computer,
echo and on https://wf-translate.silentanarchist.com once Caddy is on the wf-proxy network.
goto end
:unfilled
echo weblate.env still has CHANGE-ME in it. Fill in those settings first (see READ ME.txt).
goto end
:failed
echo Could not start Weblate. Is Docker Desktop running?
:end
echo.
pause
