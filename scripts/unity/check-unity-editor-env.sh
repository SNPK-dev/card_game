#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${UNITY_EDITOR:-}" ]]; then
  echo "SKIPPED: UNITY_EDITOR is not set. Set it to the Unity 6000.3.2f1 editor executable."
  exit 2
fi

if [[ ! -e "$UNITY_EDITOR" ]]; then
  echo "SKIPPED: UNITY_EDITOR does not exist: $UNITY_EDITOR"
  exit 2
fi

echo "UNITY_EDITOR is configured: $UNITY_EDITOR"
