@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building Keyframe Dashboard for linux/amd64 and linux/arm64/v8...
pushd "%~dp0"
docker buildx build -f dashboard\Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/keyframe-dashboard:%1 --tag jchristn77/keyframe-dashboard:latest --push dashboard
SET RC=%ERRORLEVEL%
IF NOT "%RC%" == "0" GOTO :Popd
ECHO.
ECHO Pulling jchristn77/keyframe-dashboard:%1 and jchristn77/keyframe-dashboard:latest...
docker pull jchristn77/keyframe-dashboard:%1
docker pull jchristn77/keyframe-dashboard:latest
SET RC=%ERRORLEVEL%

:Popd
popd
GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-dashboard.bat v0.1.0
SET RC=1

:Done
ECHO.
ECHO Done
EXIT /B %RC%
