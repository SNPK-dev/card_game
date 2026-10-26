#!/usr/bin/env bash
set -euo pipefail

MCP_ENDPOINT="${CARDGAME_UNITY_MCP_ENDPOINT:-http://127.0.0.1:18088}"
MCP_HEALTH_URL="${MCP_ENDPOINT%/}/health"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPECTED_PROJECT="$REPO_ROOT/clients/unity/CardGame.Unity"
EXPECTED_UNITY_VERSION="6000.3.2f1"
STARTUP_COMMAND="uvx --from mcpforunityserver==10.0.0 mcp-for-unity --transport http --http-url $MCP_ENDPOINT --project-scoped-tools"

echo "Unity MCP endpoint diagnostic"
echo "Endpoint: ${MCP_ENDPOINT}"
echo "Health URL: ${MCP_HEALTH_URL}"
echo "Expected project: ${EXPECTED_PROJECT}"
echo "Expected Unity version: ${EXPECTED_UNITY_VERSION}"

response=""
if command -v curl >/dev/null 2>&1; then
  response="$(curl --silent --show-error --fail --max-time 5 "${MCP_HEALTH_URL}" 2>/dev/null || true)"
fi

if [[ -z "${response}" ]] && command -v curl.exe >/dev/null 2>&1; then
  response="$(curl.exe --silent --show-error --fail --max-time 5 "${MCP_HEALTH_URL}" 2>/dev/null || true)"
fi

if [[ -z "${response}" ]] && command -v powershell.exe >/dev/null 2>&1; then
  response="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; (Invoke-WebRequest -UseBasicParsing -Uri '${MCP_HEALTH_URL}' -TimeoutSec 5).Content" 2>/dev/null | tr -d '\r' || true)"
fi

if [[ -z "${response}" ]]; then
  response="$(python3 - "${MCP_HEALTH_URL}" <<'PY'
import sys
import urllib.request

url = sys.argv[1]
try:
  with urllib.request.urlopen(url, timeout=5) as response:
    print(response.read().decode("utf-8", errors="replace"))
except Exception as exc:
  print(f"FAILED: {exc}", file=sys.stderr)
  raise SystemExit(1)
PY
)" || true
fi

if [[ -z "${response}" ]]; then
  echo "FAILED: unityMCP endpoint is unreachable."
  echo "Manual startup command:"
  echo "${STARTUP_COMMAND}"
  exit 1
fi

echo "Endpoint reachable."
echo "Health response: ${response}"
echo
echo "This script only checks the local MCP HTTP endpoint. Actual editor, scene,"
echo "hierarchy, console, and Play Mode inspection must still be performed through"
echo "unityMCP tools/resources and recorded in docs/unity/mcp-inspection-latest.md."
