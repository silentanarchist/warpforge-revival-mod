@echo off
rem Stops Weblate. Translations and accounts are kept (in Docker's volumes) for the next start.
cd /d "%~dp0"
docker compose stop
echo.
pause
