@echo off
rem The last lines Weblate wrote (press Ctrl+C to stop following it). Useful when it does not start.
cd /d "%~dp0"
docker compose logs --tail 200 -f weblate
