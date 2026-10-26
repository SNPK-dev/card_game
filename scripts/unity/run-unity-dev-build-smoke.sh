#!/usr/bin/env bash
set -euo pipefail

mode="${1:---validate-only}"
case "$mode" in
  --validate-only|--standalone-dev|--webgl-dev)
    exec bash scripts/unity/run-unity-release-candidate-build.sh "$mode"
    ;;
  *)
    echo "Usage: $0 [--validate-only|--standalone-dev|--webgl-dev]"
    exit 2
    ;;
esac
