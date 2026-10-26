#!/usr/bin/env bash
set -euo pipefail

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
root="artifacts/unity-rc-validation/$timestamp"
mkdir -p "$root"
summary="$root/summary.json"
steps_file="$root/steps.jsonl"

if [[ -z "${UNITY_EDITOR:-}" ]]; then
  for candidate in \
    "/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" \
    "/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" \
    "/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app/Contents/MacOS/Unity" \
    "/opt/unity/Editor/Unity"
  do
    if [[ -e "$candidate" ]]; then
      export UNITY_EDITOR="$candidate"
      break
    fi
  done
fi

record_step() {
  local name="$1"
  local status="$2"
  local exit_code="$3"
  local log_path="$4"
  local reason="${5:-}"
  printf '{"name":"%s","status":"%s","exitCode":%s,"logPath":"%s","reason":"%s"}\n' \
    "$name" "$status" "$exit_code" "$log_path" "$reason" >> "$steps_file"
}

run_step() {
  local name="$1"
  shift
  local log="$root/$name.log"
  echo "RC validation step: $name"
  set +e
  "$@" >"$log" 2>&1
  local exit_code=$?
  set -e
  if [[ $exit_code -eq 0 ]]; then
    record_step "$name" "passed" "$exit_code" "$log"
    return 0
  fi

  record_step "$name" "failed" "$exit_code" "$log" "critical command failed"
  echo "FAILED: $name (log: $log)"
  return "$exit_code"
}

run_live_playmode_smoke() {
  local server_exe="src/Game.Api/bin/Release/net10.0/Game.Api.exe"
  local storage="$root/live-playmode-data"
  local server_log="$root/live-playmode-server.log"
  local server_error="$root/live-playmode-server-error.log"
  if [[ ! -x "$server_exe" ]]; then
    echo "Release backend is missing: $server_exe" >&2
    return 1
  fi
  mkdir -p "$storage"
  "$server_exe" --urls http://127.0.0.1:5000 \
    "--Storage:RootPath=$storage" --Api:WriteRateLimit:PermitLimit=5000 \
    >"$server_log" 2>"$server_error" &
  local server_pid=$!
  cleanup_live_server() {
    kill "$server_pid" >/dev/null 2>&1 || true
    wait "$server_pid" >/dev/null 2>&1 || true
  }
  trap cleanup_live_server EXIT
  local healthy=false
  for _ in $(seq 1 30); do
    if curl --silent --fail http://127.0.0.1:5000/health | grep -q '"status":"ok"'; then
      healthy=true
      break
    fi
    sleep 1
  done
  if [[ "$healthy" != true ]]; then
    echo "Live PlayMode backend did not become healthy. See $server_error" >&2
    cleanup_live_server
    trap - EXIT
    return 1
  fi
  set +e
  env CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
  local smoke_exit=$?
  set -e
  cleanup_live_server
  trap - EXIT
  return "$smoke_exit"
}

: > "$steps_file"

run_step check-unity-editor-env bash scripts/unity/check-unity-editor-env.sh
run_step validate-openapi bash scripts/unity/validate-openapi.sh
run_step check-unity-dto-drift bash scripts/unity/check-unity-dto-drift.sh
run_step run-local-smoke env UNITY_SMOKE_DOTNET_TEST_NO_BUILD=1 bash scripts/unity/run-local-smoke.sh
run_step run-bot-match-smoke env UNITY_SMOKE_DOTNET_TEST_NO_BUILD=1 bash scripts/unity/run-bot-match-smoke.sh
run_step run-webgl-cors-smoke env UNITY_SMOKE_DOTNET_TEST_NO_BUILD=1 bash scripts/unity/run-webgl-cors-smoke.sh
run_step unity-media-inventory bash scripts/unity/check-unity-media-inventory.sh
run_step unity-quality-gates bash scripts/unity/run-unity-client-quality-gates.sh
run_step unity-dev-build-validate bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
run_step unity-dev-build-standalone bash scripts/unity/run-unity-dev-build-smoke.sh --standalone-dev
run_step unity-dev-build-webgl bash scripts/unity/run-unity-dev-build-smoke.sh --webgl-dev
run_step unity-playmode-live-smoke run_live_playmode_smoke
run_step forbidden-api-scan bash scripts/ai/forbidden-api-scan.sh
run_step openspec-release-candidate bash -lc 'if command -v cmd.exe >/dev/null 2>&1; then cmd.exe /c openspec.cmd validate unity-client-release-candidate --strict; else cmd /c openspec.cmd validate unity-client-release-candidate --strict; fi'

PYTHON="${PYTHON:-}"
if [[ -z "$PYTHON" ]]; then
  for candidate in python py python3; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" --version >/dev/null 2>&1; then
      PYTHON="$candidate"
      break
    fi
  done
  if [[ -z "$PYTHON" ]]; then
    echo "python was not found on PATH. Set PYTHON=/path/to/python and rerun." >&2
    exit 127
  fi
fi

"$PYTHON" - "$timestamp" "$steps_file" "$summary" <<'PY'
import json
import sys

timestamp, steps_path, summary_path = sys.argv[1:4]
steps = []
with open(steps_path, encoding="utf-8") as handle:
    for line in handle:
        line = line.strip()
        if line:
            steps.append(json.loads(line))
status = "passed" if all(step["status"] in ("passed", "skipped") for step in steps) else "failed"
by_name = {step["name"]: step for step in steps}
def step_status(*names):
    selected = [by_name.get(name, {"status": "missing"})["status"] for name in names]
    return "passed" if all(status in ("passed", "skipped") for status in selected) else "failed"

summary = {
    "validationId": f"unity-rc-{timestamp}",
    "status": status,
    "startedAt": timestamp,
    "steps": steps,
    "artifactRoot": str(summary_path.rsplit("/", 1)[0]),
    "backendSmokes": step_status("run-local-smoke", "run-bot-match-smoke", "run-webgl-cors-smoke"),
    "dtoDrift": step_status("check-unity-dto-drift"),
    "openApi": step_status("validate-openapi"),
    "fullLiveSmoke": step_status("unity-playmode-live-smoke"),
    "repeatability": step_status("unity-playmode-live-smoke"),
    "devBuildSmoke": step_status("unity-dev-build-validate"),
    "standaloneBuild": step_status("unity-dev-build-standalone"),
    "webglBuild": step_status("unity-dev-build-webgl"),
    "mediaInventory": step_status("unity-media-inventory"),
    "qualityGates": step_status("unity-quality-gates"),
    "mcpInspection": "see docs/unity/mcp-inspection-latest.md",
    "authorityHiddenInfo": "see docs/unity/client-authority-and-hidden-info-final-gate.md",
    "docs": step_status("openspec-release-candidate"),
    "finalVerdict": "unity-client-release-candidate-ready-with-known-limitations" if status == "passed" else "unity-client-release-candidate-blocked",
}
with open(summary_path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2)
PY

echo "Unity client RC validation summary: $summary"
