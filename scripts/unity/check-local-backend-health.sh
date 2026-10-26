#!/usr/bin/env bash
set -euo pipefail

backend_url="${UNITY_BACKEND_URL:-http://localhost:5000}"
health_url="${backend_url%/}/health"

echo "Checking backend health at $health_url"

if ! curl --fail --silent --show-error "$health_url" >/dev/null; then
  if command -v powershell.exe >/dev/null 2>&1; then
    if powershell.exe -NoProfile -Command "try { \$response = Invoke-WebRequest -UseBasicParsing '$health_url'; if (\$response.StatusCode -ge 200 -and \$response.StatusCode -lt 300) { exit 0 }; exit 1 } catch { exit 1 }"; then
      echo "Backend health check passed via Windows PowerShell."
      exit 0
    fi
  fi

  echo "Backend health check failed."
  echo "Start the backend from the repository root, for example:"
  echo "  dotnet run --project src/Game.Api"
  exit 2
fi

echo "Backend health check passed."
