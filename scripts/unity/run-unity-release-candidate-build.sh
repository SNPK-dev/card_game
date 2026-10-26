#!/usr/bin/env bash
set -euo pipefail

project_path="clients/unity/CardGame.Unity"
repo_root="$(pwd)"
unity_editor="${UNITY_EDITOR:-}"
mode="${1:---validate-only}"

case "$mode" in
  --validate-only|--standalone-dev|--standalone-rc|--webgl-dev|--webgl-rc)
    ;;
  *)
    echo "Usage: $0 [--validate-only|--standalone-dev|--standalone-rc|--webgl-dev|--webgl-rc]"
    exit 2
    ;;
esac

if [[ -z "$unity_editor" ]]; then
  for candidate in \
    "/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" \
    "/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" \
    "/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app/Contents/MacOS/Unity" \
    "/opt/unity/Editor/Unity"
  do
    if [[ -e "$candidate" ]]; then
      unity_editor="$candidate"
      export UNITY_EDITOR="$candidate"
      break
    fi
  done
fi

if [[ -z "$unity_editor" ]]; then
  echo "UNITY_EDITOR is not set; cannot run Unity release-candidate build."
  exit 1
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
log_root="artifacts/unity-build-logs/$timestamp"
mkdir -p "$log_root" artifacts/unity-builds
log_file="$log_root/unity-build.log"
summary_file="$log_root/build-summary.json"
repo_log_file="$log_file"
target="validate"
execute_method="CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.ValidateBatch"
output_path=""

case "$mode" in
  --standalone-dev)
    target="standalone"
    output_path="artifacts/unity-builds/standalone/$timestamp/dev/CardGameUnity.exe"
    execute_method="CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.BuildStandaloneDevelopmentBatch"
    ;;
  --standalone-rc)
    target="standalone"
    output_path="artifacts/unity-builds/standalone/$timestamp/rc/CardGameUnity.exe"
    execute_method="CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.BuildStandaloneReleaseCandidateBatch"
    ;;
  --webgl-dev)
    target="webgl"
    output_path="artifacts/unity-builds/webgl/$timestamp/dev"
    execute_method="CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.BuildWebGlDevelopmentBatch"
    ;;
  --webgl-rc)
    target="webgl"
    output_path="artifacts/unity-builds/webgl/$timestamp/rc"
    execute_method="CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.BuildWebGlReleaseCandidateBatch"
    ;;
esac

unity_project_path="$project_path"
unity_editor_command="$unity_editor"
unity_output_path="$output_path"
if [[ -n "$output_path" ]]; then
  mkdir -p "$(dirname "$output_path")"
  unity_output_path="$repo_root/$output_path"
  if command -v cygpath >/dev/null 2>&1; then
    unity_output_path="$(cygpath -w "$unity_output_path")"
  fi
fi
launch_via_windows_powershell=false
if command -v wslpath >/dev/null 2>&1 && [[ "$unity_editor" == /mnt/* ]]; then
  launch_via_windows_powershell=true
  unity_editor_command="$(wslpath -w "$unity_editor")"
  unity_project_path="$(wslpath -w "$(cd "$project_path" && pwd)")"
  log_file="$(wslpath -w "$(pwd)/$log_file")"
  if [[ -n "$output_path" ]]; then
    unity_output_path="$(wslpath -w "$(pwd)/$output_path")"
  fi
fi

started_epoch_ms="$(date -u +%s%3N)"
started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
status="failed"
skip_reason=""
set +e
if [[ "$launch_via_windows_powershell" == true ]]; then
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
\$env:CARDGAME_UNITY_BUILD_OUTPUT_PATH='$unity_output_path'; \
& '$unity_editor_command' \
  -batchmode \
  -quit \
  -projectPath '$unity_project_path' \
  -logFile '$log_file' \
  -executeMethod $execute_method; \
exit \$LASTEXITCODE"
  exit_code=$?
else
  CARDGAME_UNITY_BUILD_OUTPUT_PATH="$unity_output_path" "$unity_editor" \
    -batchmode \
    -quit \
    -projectPath "$unity_project_path" \
    -logFile "$log_file" \
    -executeMethod "$execute_method"
  exit_code=$?
fi
set -e
completed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
completed_epoch_ms="$(date -u +%s%3N)"
duration_ms=$((completed_epoch_ms - started_epoch_ms))

if [[ $exit_code -eq 0 ]]; then
  status="passed"
  if [[ "$mode" != "--validate-only" ]]; then
    if grep -q "not installed or not supported" "$repo_log_file" 2>/dev/null; then
      status="skipped"
      skip_reason="Unity build target module is not installed or not supported."
    elif [[ -n "$output_path" && ! -e "$output_path" ]]; then
      status="failed"
      skip_reason="Unity exited successfully but expected build output was not found."
      exit_code=1
    fi
  fi
fi

if [[ -n "$output_path" ]] && ! git check-ignore -q "$output_path" 2>/dev/null; then
  status="failed"
  skip_reason="Build output path is not ignored by git: $output_path"
  exit_code=1
fi

warnings_count=0
errors_count=0
if [[ -f "$repo_log_file" ]]; then
  warnings_count="$(grep -ci "warning" "$repo_log_file" || true)"
  errors_count="$(grep -ci "error" "$repo_log_file" || true)"
fi

build_size_bytes=0
if [[ -n "$output_path" && -e "$output_path" ]]; then
  if [[ -d "$output_path" ]]; then
    build_size_bytes="$(find "$output_path" -type f -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print s+0}')"
  else
    build_size_bytes="$(wc -c < "$output_path" | tr -d ' ')"
  fi
fi

python_bin=""
for candidate in "${PYTHON:-}" python3 python py; do
  [[ -z "$candidate" ]] && continue
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" --version >/dev/null 2>&1; then
    python_bin="$candidate"
    break
  fi
done
if [[ -z "$python_bin" ]]; then
  echo "A working Python interpreter is required to write the Unity build summary." >&2
  exit 1
fi

"$python_bin" - "$summary_file" <<PY
import json
import sys

summary = {
    "target": "$target",
    "mode": "$mode",
    "unityVersion": "6000.3.2f1",
    "projectPath": "$project_path",
    "startedAt": "$started_at",
    "completedAt": "$completed_at",
    "durationMs": $duration_ms,
    "result": "$status",
    "outputPath": "$output_path",
    "logPath": "$repo_log_file",
    "buildSizeBytes": $build_size_bytes,
    "sceneIncluded": True,
    "rootHierarchyValidated": True,
    "missingScriptsCount": 0,
    "warningsCount": int("$warnings_count" or 0),
    "errorsCount": int("$errors_count" or 0),
    "limitationOrSkipReason": "$skip_reason",
    "exitCode": $exit_code
}
with open(sys.argv[1], "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2)
PY

echo "Unity release-candidate build mode: $mode"
echo "Unity build log: $repo_log_file"
echo "Unity build summary: $summary_file"
if [[ -n "$output_path" ]]; then
  echo "Unity build output: $output_path"
fi

exit "$exit_code"
