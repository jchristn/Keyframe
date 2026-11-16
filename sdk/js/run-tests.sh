#!/usr/bin/env bash
# Run the JavaScript SDK harness against a running Keyframe server.
# Reads KEYFRAME_URL, KEYFRAME_ADMIN_KEY, KEYFRAME_ACCESS_KEY.
set -euo pipefail
cd "$(dirname "$0")"
npm test
