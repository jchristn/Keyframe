#!/bin/bash
# Pull the latest images, recreate the Keyframe stack, and report container status.
# Non-destructive: named volumes (database, logs, Prometheus, Tempo, Loki, Grafana) are preserved.
# Every optional model container (profiles ollama, listen, listen-gpu) is stopped; they come back up when
# COMPOSE_PROFILES names them, for example COMPOSE_PROFILES=listen. Their model directories are kept.
# For a destructive reset to factory defaults, use factory/reset.sh.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

# Keyframe images come from Docker Hub (jchristn77/keyframe-*). Build and push them with build-all.sh.
docker compose pull
docker compose --profile ollama --profile listen --profile listen-gpu down
docker compose up -d
docker ps -a
