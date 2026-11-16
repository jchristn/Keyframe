# Keyframe server image.
#
# Build from the repository root so the build can reach src/ and assets/:
#   docker build -f src/Keyframe.Server/Dockerfile -t jchristn77/keyframe-server:v0.1.0 .
#
# Runtime layout:
#   /app                       published server (includes assets/glyph.ico)
#   /app/config/keyframe.json  settings (KEYFRAME_SETTINGS_FILE); mount your own file here
#   /app/data                  SQLite database and other persistent state (volume)
#   /app/temp                  media working directory (volume)
#   /app/logs                  log files (volume)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first so package downloads are cached independently of source changes.
COPY src/Directory.Build.props src/
COPY src/Keyframe.Core/Keyframe.Core.csproj src/Keyframe.Core/
COPY src/Keyframe.Server/Keyframe.Server.csproj src/Keyframe.Server/
RUN dotnet restore src/Keyframe.Server/Keyframe.Server.csproj

COPY assets/glyph.ico assets/
COPY src/Keyframe.Core/ src/Keyframe.Core/
COPY src/Keyframe.Server/ src/Keyframe.Server/
RUN dotnet publish src/Keyframe.Server/Keyframe.Server.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        -p:UseAppHost=false \
    && test -f /app/publish/assets/glyph.ico \
    && rm -f /app/publish/keyframe.json

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# ffmpeg and ffprobe drive the media pipeline; curl backs the container healthcheck.
RUN set -eux; \
    printf '%s\n' 'Acquire::Retries "5";' 'Acquire::http::Timeout "30";' 'Acquire::https::Timeout "30";' \
        > /etc/apt/apt.conf.d/80-retries; \
    apt-get update; \
    apt-get install -y --no-install-recommends ffmpeg curl ca-certificates; \
    rm -rf /var/lib/apt/lists/*; \
    ffmpeg -hide_banner -version | head -n 1; \
    ffprobe -hide_banner -version | head -n 1

WORKDIR /app
RUN mkdir -p /app/config /app/data /app/temp /app/logs \
    && chown -R ${APP_UID}:${APP_UID} /app

COPY --from=build --chown=${APP_UID}:${APP_UID} /app/publish/ /app/
# Container defaults: listen on all interfaces, paths under /app, OTLP export to the compose collector.
COPY --chown=${APP_UID}:${APP_UID} docker/keyframe.json /app/config/keyframe.json

ENV KEYFRAME_SETTINGS_FILE=/app/config/keyframe.json \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    TERM=xterm

VOLUME ["/app/data", "/app/temp", "/app/logs"]

# REST API and the in-process Prometheus scrape endpoint.
EXPOSE 8700 9464

# Run as the non-root "app" user that ships with the .NET images.
USER ${APP_UID}

HEALTHCHECK --interval=5s --timeout=3s --retries=2 --start-period=20s \
    CMD ["curl", "-fsS", "http://127.0.0.1:8700/v1.0/health"]

ENTRYPOINT ["dotnet", "Keyframe.Server.dll"]
