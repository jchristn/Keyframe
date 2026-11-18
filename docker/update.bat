@echo off
rem Pull the latest images, recreate the Keyframe stack, and report container status.
rem Non-destructive: named volumes (database, logs, Prometheus, Tempo, Loki, Grafana) are preserved.
rem Every optional model container (profiles ollama, listen, listen-gpu) is stopped; they come back up when
rem COMPOSE_PROFILES names them, for example COMPOSE_PROFILES=listen. Their model directories are kept.
rem For a destructive reset to factory defaults, use factory\reset.bat.

setlocal

cd /d "%~dp0"

rem Keyframe images come from Docker Hub (jchristn77/keyframe-*). Build and push them with build-all.bat.
docker compose pull
if errorlevel 1 exit /b %errorlevel%

docker compose --profile ollama --profile listen --profile listen-gpu down
if errorlevel 1 exit /b %errorlevel%

docker compose up -d
if errorlevel 1 exit /b %errorlevel%

docker ps -a

endlocal
