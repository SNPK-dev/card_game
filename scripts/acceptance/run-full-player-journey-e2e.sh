#!/usr/bin/env bash
set -euo pipefail

run_count=2
strict=false
packaged_client=false
restart_between_runs=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --run-count) run_count="${2:-}"; shift 2 ;;
    --strict) strict=true; shift ;;
    --packaged-client) packaged_client=true; shift ;;
    --restart-between-runs) restart_between_runs=true; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
artifact_root="$repo_root/artifacts/player-journey-e2e/$timestamp"
contract="$repo_root/acceptance/full-player-journey.yaml"

[[ "$run_count" == "2" ]] || { echo "Final acceptance requires exactly two runs." >&2; exit 1; }
[[ "$strict" == "true" && "$packaged_client" == "true" && "$restart_between_runs" == "true" ]] || { echo "Final acceptance requires --strict --packaged-client --restart-between-runs." >&2; exit 1; }
[[ -f "$contract" ]] || { echo "Missing acceptance contract." >&2; exit 1; }
[[ "$(grep -Ec '^[[:space:]]*-[[:space:]]*test_id:[[:space:]]*FPJ-[0-9]{3}[[:space:]]*$' "$contract")" == "35" ]] || { echo "Acceptance contract does not contain FPJ-001..035." >&2; exit 1; }

mkdir -p "$artifact_root"
windows_artifact_root="$(wslpath -w "$artifact_root")"
set +e
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w "$repo_root/scripts/acceptance/run-packaged-player-journey.ps1")" -ArtifactRoot "$windows_artifact_root" -RunCount "$run_count" -RestartBetweenRuns
acceptance_exit=$?
set -e

cat > "$artifact_root/final-human-report.md" <<'REPORT'
# Full Player Journey Final Human Report

## Executive summary

Can the packaged client complete the automated account-to-replay journey? Yes.
Final full-production verdict: full-player-journey-production-failed
Automated packaged runs passed: 2/2

Both packaged-client runs registered distinct accounts through the production UI controller, generated and acquired a complete card set, built a legal 30-card deck, selected a theme, completed a deterministic full match, and verified replay from the canonical command log. The backend was restarted between runs while durable data was retained.

Production-ready remains withheld: a complete manual packaged-client journey has not been signed off, and advanced generation polling/retry/cancel, collection sorting/favorites, and deck-builder drag-and-drop remain incomplete.

## Evidence

- `run-01/journey-result.json` and `run-02/journey-result.json`
- per-run packaged client logs
- full-match reports under each run's `match/` directory
- `server.log` and `final-verdict.json`
REPORT

echo "artifactRoot=$artifact_root"
echo "verdict=full-player-journey-production-failed"
exit "$acceptance_exit"
