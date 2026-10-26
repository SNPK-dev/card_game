#!/usr/bin/env bash
set -euo pipefail

project_path="${UNITY_PROJECT_PATH:-clients/unity/CardGame.Unity}"
unity_editor="${UNITY_EDITOR:-}"
log_root="${UNITY_TEST_LOG_ROOT:-artifacts/unity-test-logs}"
seed_mode="${CARDGAME_UNITY_SEED_MODE:-Ensure}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
log_file="$log_root/playmode-smoke-$timestamp.log"
results_file="$log_root/playmode-smoke-$timestamp-results.xml"

mkdir -p "$log_root"

unity_project_path="$project_path"
unity_log_file="$log_file"
unity_results_file="$results_file"
unity_editor_command="$unity_editor"
launch_via_windows_powershell=false
if command -v cygpath >/dev/null 2>&1 && [[ "$unity_editor" == *.exe ]]; then
  unity_project_path="$(cygpath -w "$(cd "$project_path" && pwd)")"
  unity_log_file="$(cygpath -w "$(pwd)/$log_file")"
  unity_results_file="$(cygpath -w "$(pwd)/$results_file")"
fi
if command -v wslpath >/dev/null 2>&1 && [[ "$unity_editor" == /mnt/* ]]; then
  launch_via_windows_powershell=true
  unity_editor_command="$(wslpath -w "$unity_editor")"
  unity_project_path="$(wslpath -w "$(cd "$project_path" && pwd)")"
  unity_log_file="$(wslpath -w "$(mkdir -p "$(dirname "$log_file")" && cd "$(dirname "$log_file")" && pwd)")\\$(basename "$log_file")"
  unity_results_file="$(wslpath -w "$(mkdir -p "$(dirname "$results_file")" && cd "$(dirname "$results_file")" && pwd)")\\$(basename "$results_file")"
fi

echo "Unity PlayMode full live smoke"
echo "Prerequisite: start the backend first, normally with: dotnet run --project src/Game.Api"
echo "Project: $project_path"
echo "Seed mode: $seed_mode"

if [[ -z "$unity_editor" ]]; then
  echo "SKIPPED: UNITY_EDITOR is not set; cannot run Unity PlayMode smoke in batchmode." | tee "$log_file"
  exit 2
fi

if [[ "$launch_via_windows_powershell" == true ]]; then
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
\$env:CARDGAME_UNITY_LIVE_BACKEND_TESTS='1'; \
\$env:CARDGAME_UNITY_SEED_MODE='$seed_mode'; \
\$env:UNITY_BATCHMODE_LOG_PATH='$log_file'; \
\$env:UNITY_TEST_RESULTS_PATH='$results_file'; \
& '$unity_editor_command' \
  -batchmode \
  -projectPath '$unity_project_path' \
  -runTests \
  -testPlatform PlayMode \
  -testResults '$unity_results_file' \
  -logFile '$unity_log_file'; \
exit \$LASTEXITCODE"
else
  CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 \
  CARDGAME_UNITY_SEED_MODE="$seed_mode" \
  UNITY_BATCHMODE_LOG_PATH="$log_file" \
  UNITY_TEST_RESULTS_PATH="$results_file" \
  "$unity_editor" \
    -batchmode \
    -projectPath "$unity_project_path" \
    -runTests \
    -testPlatform PlayMode \
    -testResults "$unity_results_file" \
    -logFile "$unity_log_file"
fi

if [[ ! -f "$results_file" ]]; then
  echo "FAILED: Unity PlayMode smoke did not write test results: $results_file"
  echo "Unity log: $log_file"
  exit 1
fi

if ! grep -q "LiveBackend_FullLocalMatchFlow_RunsWhenEnabled" "$results_file"; then
  echo "FAILED: Unity PlayMode smoke results did not include the live backend smoke test."
  echo "Unity results: $results_file"
  exit 1
fi

if ! grep -q 'LiveBackend_FullLocalMatchFlow_RunsWhenEnabled".*result="Passed"' "$results_file"; then
  echo "FAILED: Unity PlayMode live backend smoke test did not pass."
  echo "Unity results: $results_file"
  exit 1
fi

echo "Unity PlayMode smoke results: $results_file"
echo "Unity PlayMode smoke log: $log_file"
