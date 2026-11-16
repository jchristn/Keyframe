@echo off
rem Factory reset for the Keyframe Docker stack. DESTRUCTIVE.
rem
rem Stops the stack, deletes its state volumes (database, media temp, logs, Prometheus, Tempo, Loki, Grafana),
rem removes any local data directories, restores keyframe.json from the factory copy (Ollama and Whisper endpoints,
rem default keys), and brings the stack back up. Downloaded models are kept: the Whisper model volume
rem (keyframe_whisper-models) and the Ollama models in docker\ollama-models. Delete those by hand to reclaim space.
rem
rem Usage: reset.bat [-y]    (-y skips the confirmation prompt)

setlocal

set "FACTORY_DIR=%~dp0"
set "DOCKER_DIR=%FACTORY_DIR%.."
set "COMPOSE_FILE=%DOCKER_DIR%\compose.yaml"

echo.
echo ============================================
echo   Keyframe Factory Reset
echo ============================================
echo.
echo WARNING: This resets Keyframe to factory defaults.
echo All jobs, request history, logs, metrics, traces, Grafana edits,
echo and settings changes will be lost.
echo.

if /i "%~1"=="-y" goto confirmed
set /p confirm="Are you sure? (y/N): "
if /i not "%confirm%"=="y" (
    echo Cancelled.
    exit /b 0
)

:confirmed
echo.
echo Stopping containers...
call docker compose -f "%COMPOSE_FILE%" --profile ollama down --remove-orphans
if errorlevel 1 echo Warning: docker compose down failed; continuing with the local reset.

echo.
echo Deleting state volumes (model caches are kept)...
for %%V in (data temp logs prometheus tempo loki grafana) do (
    docker volume rm keyframe_%%V >nul 2>&1 && echo   removed keyframe_%%V
)

echo.
echo Removing local data directories...
if exist "%DOCKER_DIR%\data" rd /S /Q "%DOCKER_DIR%\data"
if exist "%DOCKER_DIR%\logs" rd /S /Q "%DOCKER_DIR%\logs"
if exist "%DOCKER_DIR%\temp" rd /S /Q "%DOCKER_DIR%\temp"

echo.
echo Restoring factory configuration...
copy /Y "%FACTORY_DIR%keyframe.json" "%DOCKER_DIR%\keyframe.json" >nul
if errorlevel 1 (
    echo Failed to restore factory configuration.
    exit /b 1
)

echo.
echo Starting the stack...
call docker compose -f "%COMPOSE_FILE%" up -d
if errorlevel 1 exit /b %errorlevel%

echo.
echo ============================================
echo   Reset complete.
echo   Server:    http://127.0.0.1:8700
echo   Dashboard: http://127.0.0.1:8701
echo   Grafana:   http://127.0.0.1:3700 (admin / admin)
echo   Samples:   %FACTORY_DIR%samples
echo ============================================
echo.

endlocal
