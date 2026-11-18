#!/bin/bash
# Factory reset for the Keyframe Docker stack. DESTRUCTIVE.
#
# Stops the stack, deletes its state volumes (database, media temp, logs, Prometheus, Tempo, Loki, Grafana),
# removes any local data directories, restores keyframe.json from the factory copy (Ollama, Whisper, and listening
# endpoints, default keys), and brings the stack back up. Downloaded models are kept: the Whisper model volume
# (keyframe_whisper-models), the Ollama models in docker/ollama-models, the llama-server models in
# docker/llama-models, and the vLLM models in docker/vllm-models. Delete those by hand to reclaim space.
# Optional model containers come back up when COMPOSE_PROFILES names them (for example listen or ollama,listen).
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
echo "Stopping containers..."
if ! docker compose -f "${compose_file}" --profile ollama --profile listen --profile listen-gpu down --remove-orphans; then
    echo "Warning: docker compose down failed; continuing with the local reset."
fi

echo ""
echo "Deleting state volumes (model caches are kept)..."
for v in data temp logs prometheus tempo loki grafana; do
    docker volume rm "keyframe_${v}" > /dev/null 2>&1 && echo "  removed keyframe_${v}" || true
done

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
