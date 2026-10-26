#!/usr/bin/env bash
set -euo pipefail

backend_url="${UNITY_BACKEND_URL:-http://localhost:5000}"

if ! bash scripts/unity/check-unity-editor-env.sh; then
  exit 2
fi

bash scripts/unity/check-local-backend-health.sh

echo "Running backend local smoke before Unity PlayMode smoke."
bash scripts/unity/run-local-smoke.sh

echo "Running Unity full live PlayMode smoke."
bash scripts/unity/run-unity-playmode-smoke.sh

echo "Unity live smoke reports are written under artifacts/unity-client-live-smoke/."
echo "Repeatability reports are written under artifacts/unity-client-live-smoke-repeatability/ when the guarded repeatability test runs."
