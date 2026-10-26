#!/usr/bin/env bash
set -euo pipefail

if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/install-local-models.ps1" "$@"
elif command -v powershell >/dev/null 2>&1; then
  powershell -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/install-local-models.ps1" "$@"
else
  echo "PowerShell is required to run install-local-models.ps1." >&2
  exit 1
fi
