#!/usr/bin/env bash
set -e

if [ -z "$1" ]; then
  echo "Provide a tag argument for the build."
  echo "Example: ./build-mcp.sh v0.1.0"
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo
echo "Building Keyframe MCP Server for linux/amd64 and linux/arm64/v8..."
cd "$SCRIPT_DIR"
docker buildx build -f src/Keyframe.McpServer/Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag jchristn77/keyframe-mcp:"$1" --tag jchristn77/keyframe-mcp:latest --push .

echo
echo "Pulling jchristn77/keyframe-mcp:$1 and jchristn77/keyframe-mcp:latest..."
docker pull jchristn77/keyframe-mcp:"$1"
docker pull jchristn77/keyframe-mcp:latest

echo
echo "Done"
