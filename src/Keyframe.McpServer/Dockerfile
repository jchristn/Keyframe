# Keyframe MCP server image.
#
# Build from the repository root so the build can reach the C# SDK:
#   docker build -f src/Keyframe.McpServer/Dockerfile -t jchristn77/keyframe-mcp:v0.1.0 .
#
# Configure with environment variables: KEYFRAME_URL, KEYFRAME_ACCESS_KEY (also the bearer token MCP clients send),
# KEYFRAME_MCP_HOSTNAME ("*" inside containers), and KEYFRAME_MCP_REQUIRE_AUTHENTICATION.
# Or mount a settings file and set KEYFRAME_MCP_SETTINGS_FILE.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY src/Directory.Build.props src/
COPY sdk/csharp/Keyframe.Sdk/Keyframe.Sdk.csproj sdk/csharp/Keyframe.Sdk/
COPY src/Keyframe.McpServer/Keyframe.McpServer.csproj src/Keyframe.McpServer/
RUN dotnet restore src/Keyframe.McpServer/Keyframe.McpServer.csproj

COPY sdk/csharp/Keyframe.Sdk/ sdk/csharp/Keyframe.Sdk/
COPY src/Keyframe.McpServer/ src/Keyframe.McpServer/
RUN dotnet publish src/Keyframe.McpServer/Keyframe.McpServer.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        -p:UseAppHost=false \
    && rm -f /app/publish/keyframe-mcp.json

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates; \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
RUN mkdir -p /app/config /app/logs && chown -R ${APP_UID}:${APP_UID} /app
COPY --from=build --chown=${APP_UID}:${APP_UID} /app/publish/ /app/

ENV KEYFRAME_MCP_SETTINGS_FILE=/app/config/keyframe-mcp.json \
    KEYFRAME_MCP_HOSTNAME=* \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TERM=xterm

# HTTP (Streamable HTTP /mcp and JSON-RPC /rpc), TCP, WebSocket.
EXPOSE 8702 8703 8704

USER ${APP_UID}
HEALTHCHECK --interval=5s --timeout=3s --retries=2 CMD curl -fsS http://127.0.0.1:8702/ || exit 1
ENTRYPOINT ["dotnet", "Keyframe.McpServer.dll"]
