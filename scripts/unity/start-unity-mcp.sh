#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
command -v uvx >/dev/null || { echo "Install uv (uvx must be on PATH)." >&2; exit 1; }
endpoint="${CARDGAME_UNITY_MCP_ENDPOINT:-http://127.0.0.1:18088}"
exec uvx --from mcpforunityserver==10.0.0 mcp-for-unity \
  --transport http --http-url "$endpoint" --project-scoped-tools
