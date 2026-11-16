@ECHO OFF
IF "%1" == "" GOTO :Usage
ECHO.
ECHO Building Keyframe MCP Server for linux/amd64 and linux/arm64/v8...
pushd "%~dp0"
docker buildx build -f src\Keyframe.McpServer\Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/keyframe-mcp:%1 --tag jchristn77/keyframe-mcp:latest --push .
SET RC=%ERRORLEVEL%
IF NOT "%RC%" == "0" GOTO :Popd
ECHO.
ECHO Pulling jchristn77/keyframe-mcp:%1 and jchristn77/keyframe-mcp:latest...
docker pull jchristn77/keyframe-mcp:%1
docker pull jchristn77/keyframe-mcp:latest
SET RC=%ERRORLEVEL%

:Popd
popd
GOTO :Done

:Usage
ECHO Provide a tag argument for the build.
ECHO Example: build-mcp.bat v0.1.0
SET RC=1

:Done
ECHO.
ECHO Done
EXIT /B %RC%
