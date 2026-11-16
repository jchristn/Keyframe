#!/usr/bin/env bash
# Run the Python SDK harness against a running Keyframe server.
# Reads KEYFRAME_URL, KEYFRAME_ADMIN_KEY, KEYFRAME_ACCESS_KEY.
set -euo pipefail
cd "$(dirname "$0")"
if [ ! -d .venv ]; then python3 -m venv .venv; fi
. .venv/bin/activate
pip install --quiet -e ".[test]"
pytest -v
