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

DEFAULT_ARTIFACT_ROOT="$ROOT/artifacts/unity-integration-smoke"
if [[ -z "${UNITY_SMOKE_ARTIFACT_ROOT:-}" ]]; then
  if [[ "$DEFAULT_ARTIFACT_ROOT" =~ ^/mnt/([a-zA-Z])/(.*)$ ]]; then
    drive="${BASH_REMATCH[1]^^}"
    tail="${BASH_REMATCH[2]//\//\\}"
    DEFAULT_ARTIFACT_ROOT="${drive}:\\${tail}"
  elif command -v wslpath >/dev/null 2>&1 && "$DOTNET" --info 2>/dev/null | grep -qi "OS Platform: Windows"; then
    DEFAULT_ARTIFACT_ROOT="$(wslpath -w "$DEFAULT_ARTIFACT_ROOT")"
  elif command -v cygpath >/dev/null 2>&1; then
    DEFAULT_ARTIFACT_ROOT="$(cygpath -w "$DEFAULT_ARTIFACT_ROOT")"
  fi
  export UNITY_SMOKE_ARTIFACT_ROOT="$DEFAULT_ARTIFACT_ROOT"
fi

test_args=(
  test tests/Game.Integration.Tests/Game.Integration.Tests.csproj
  --configuration Release
  --filter "FullyQualifiedName~ReferenceHeadlessClientDryRunTests.UnityTwoBotPublicApiFullMatchSmokeCompletesAndWritesReport"
)

if [[ "${UNITY_SMOKE_DOTNET_TEST_NO_BUILD:-0}" == "1" ]]; then
  test_args+=(--no-build)
fi

"$DOTNET" "${test_args[@]}"

echo "Unity bot match smoke report root: $UNITY_SMOKE_ARTIFACT_ROOT"
