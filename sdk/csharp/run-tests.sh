#!/usr/bin/env bash
# Run the C# SDK harness against a running Keyframe server.
# Reads KEYFRAME_URL, KEYFRAME_ADMIN_KEY, KEYFRAME_ACCESS_KEY (defaults: http://127.0.0.1:8710, keyframeadmin, default).
set -euo pipefail
cd "$(dirname "$0")"
for framework in net10.0 net8.0; do
  echo "== Keyframe.Sdk harness ($framework)"
  dotnet run --project Test.Automated -c Release -f "$framework"
done
