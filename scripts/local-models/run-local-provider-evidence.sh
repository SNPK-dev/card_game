#!/usr/bin/env bash
set -euo pipefail

if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/run-local-provider-evidence.ps1" "$@"
elif command -v powershell >/dev/null 2>&1; then
  powershell -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/run-local-provider-evidence.ps1" "$@"
else
  echo "PowerShell is required to run run-local-provider-evidence.ps1." >&2
  exit 1
fi
