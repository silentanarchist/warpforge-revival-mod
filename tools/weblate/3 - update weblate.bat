@echo off
rem Downloads the newest Weblate images and restarts with them. Translations and accounts are kept.
rem Make a backup first (Weblate: Manage > Backups) when a big version change is announced.
cd /d "%~dp0"
docker network inspect wf-proxy >nul 2>&1 || docker network create wf-proxy >nul
docker compose pull
if errorlevel 1 goto failed
docker compose up -d
goto end
:failed
echo Could not download the images. Is Docker Desktop running and the internet reachable?
:end
echo.
pause
