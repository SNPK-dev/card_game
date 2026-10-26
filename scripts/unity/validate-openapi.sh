#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

if command -v npx >/dev/null 2>&1; then
  npx --yes @redocly/cli@1.25.15 lint docs/api/openapi.yaml
elif command -v cmd.exe >/dev/null 2>&1; then
  cmd.exe /c "npx --yes @redocly/cli@1.25.15 lint docs/api/openapi.yaml"
else
  echo "npx is required to validate docs/api/openapi.yaml with Redocly CLI." >&2
  exit 127
fi
