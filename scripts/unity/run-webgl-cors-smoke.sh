#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

DOTNET="${DOTNET:-}"
if [[ -z "$DOTNET" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET="dotnet"
  elif command -v dotnet.exe >/dev/null 2>&1; then
    DOTNET="dotnet.exe"
  else
    echo "dotnet was not found on PATH. Set DOTNET=/path/to/dotnet and rerun." >&2
    exit 127
  fi
fi

test_args=(
  test tests/Game.Integration.Tests/Game.Integration.Tests.csproj
  --configuration Release
  --filter "FullyQualifiedName~Cors"
)

if [[ "${UNITY_SMOKE_DOTNET_TEST_NO_BUILD:-0}" == "1" ]]; then
  test_args+=(--no-build)
fi

"$DOTNET" "${test_args[@]}"
