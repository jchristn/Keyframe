#!/bin/bash
# Factory reset for the Keyframe Docker stack. DESTRUCTIVE.
#
# Stops the stack, deletes its named volumes (database, media temp, logs, Prometheus, Tempo, Loki, Grafana,
# Whisper and Ollama models), removes any local data directories, restores keyframe.json from the factory copy (Ollama
# and Whisper endpoints, default keys), and brings the stack back up.
#
# Usage: ./reset.sh [-y]    (-y skips the confirmation prompt)

set -euo pipefail

factory_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
docker_dir="$(cd "${factory_dir}/.." && pwd)"
compose_file="${docker_dir}/compose.yaml"

echo ""
echo "============================================"
echo "  Keyframe Factory Reset"
echo "============================================"
echo ""
echo "WARNING: This resets Keyframe to factory defaults."
echo "All jobs, request history, logs, metrics, traces, Grafana edits,"
echo "and settings changes will be lost."
echo ""

if [ "${1:-}" != "-y" ]; then
    read -r -p "Are you sure? (y/N): " confirm
    if [ "${confirm}" != "y" ] && [ "${confirm}" != "Y" ]; then
        echo "Cancelled."
        exit 0
    fi
fi

echo ""
echo "Stopping containers and deleting volumes..."
if ! docker compose -f "${compose_file}" --profile ollama down -v --remove-orphans; then
    echo "Warning: docker compose down failed; continuing with the local reset."
fi

echo ""
echo "Removing local data directories..."
rm -rf "${docker_dir}/data" "${docker_dir}/logs" "${docker_dir}/temp"

echo ""
echo "Restoring factory configuration..."
cp -f "${factory_dir}/keyframe.json" "${docker_dir}/keyframe.json"
# The server runs as a non-root user and rewrites this file when settings are saved from the dashboard.
chmod a+rw "${docker_dir}/keyframe.json"

echo ""
echo "Starting the stack..."
docker compose -f "${compose_file}" up -d

echo ""
echo "============================================"
echo "  Reset complete."
echo "  Server:    http://127.0.0.1:8700"
echo "  Dashboard: http://127.0.0.1:8701"
echo "  Grafana:   http://127.0.0.1:3700 (admin / admin)"
echo "  Samples:   ${factory_dir}/samples"
echo "============================================"
echo ""
