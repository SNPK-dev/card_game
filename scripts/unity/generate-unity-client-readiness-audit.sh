#!/usr/bin/env bash
set -euo pipefail

output_root="${1:-artifacts/unity-readiness-audit}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$output_root/$timestamp"
output="$output_root/$timestamp/client-production-readiness-audit.json"

python_bin="${PYTHON:-python3}"
if ! command -v "$python_bin" >/dev/null 2>&1; then
  python_bin="python"
fi

"$python_bin" - "$output" <<'PY'
import json
import sys
from datetime import datetime, timezone

categories = [
    ("Backend/API integration", "ready", "docs/api/openapi.yaml", "bash scripts/unity/validate-openapi.sh", "Local backend deployment is separate.", "Unity client / backend API owners"),
    ("Full live gameplay flow", "ready", "artifacts/unity-test-logs/", "CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh", "Requires local backend.", "Unity client"),
    ("Repeatability", "ready", "artifacts/unity-test-logs/", "CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh", "Proof is smoke-scope, not exhaustive balance proof.", "Unity client"),
    ("RC validation", "ready", "artifacts/unity-rc-validation/", "bash scripts/unity/run-unity-client-rc-validation.sh", "Local machine dependencies can affect Unity module availability.", "Unity client"),
    ("MCP inspection", "ready-with-limitations", "docs/unity/mcp-inspection-latest.md", "bash scripts/unity/check-unity-mcp-endpoint.sh plus MCP tools", "Active editor attachment is environment-controlled.", "Unity client"),
    ("Standalone build", "ready", "artifacts/unity-build-logs/", "bash scripts/unity/run-unity-release-candidate-build.sh --standalone-rc", "Development machine module required.", "Unity client"),
    ("WebGL build", "ready", "artifacts/unity-build-logs/", "bash scripts/unity/run-unity-release-candidate-build.sh --webgl-rc", "Hosting/deployment out of scope.", "Unity client"),
    ("Scene/prefab integrity", "ready", "artifacts/unity-build-logs/", "bash scripts/unity/run-unity-release-candidate-build.sh --validate-only", "Runtime-generated UI uses bootstrapper ownership.", "Unity client"),
    ("UI/UX polish", "ready-with-limitations", "docs/unity/client-polished-gameplay-presentation.md", "bash scripts/unity/run-unity-client-quality-gates.sh", "Generated placeholder art remains.", "Unity client / art"),
    ("Presentation animation", "ready-with-limitations", "docs/unity/event-animation-map.md", "dotnet test --configuration Release", "Animation is deterministic placeholder presentation.", "Unity client"),
    ("Audio routing", "ready-with-limitations", "docs/unity/client-audio-direction.md", "bash scripts/unity/check-unity-media-inventory.sh", "Generated cue refs, not final mastered audio.", "Unity client / audio"),
    ("Hidden-info safety", "ready", "docs/unity/client-authority-and-hidden-info-final-gate.md", "CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh", "Future UI views must keep this gate.", "Unity client"),
    ("Failure/recovery UX", "ready-with-limitations", "docs/unity/client-release-candidate.md", "dotnet test --configuration Release", "Manual visual review still recommended.", "Unity client"),
    ("Playtest evidence", "ready-with-limitations", "docs/unity/examples/client-rc-playtest-report.example.json", "manual export or smoke evidence", "Manual tester notes may be absent.", "QA"),
    ("Asset/license safety", "ready", "docs/unity/client-media-inventory.md", "bash scripts/unity/check-unity-media-inventory.sh", "Future external media must add license metadata.", "Unity client / art"),
    ("Performance baseline", "ready-with-limitations", "docs/unity/client-performance-baseline.md", "bash scripts/unity/run-unity-client-quality-gates.sh", "No full profiler budget yet.", "Unity client"),
    ("Accessibility/readability baseline", "ready-with-limitations", "docs/unity/client-readability-accessibility-baseline.md", "bash scripts/unity/run-unity-client-quality-gates.sh", "Formal accessibility audit future.", "UX"),
    ("Documentation", "ready", "docs/unity/README.md", "cmd /c openspec.cmd validate unity-client-release-candidate --strict", "Keep examples synchronized.", "Unity client"),
    ("Known limitations", "ready-with-limitations", "docs/unity/client-release-candidate.md", "review docs", "Final media/live services out of scope.", "Product")
]

report = {
    "schemaVersion": "unity-client-production-readiness-audit.v1",
    "generatedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    "claim": "release-candidate-ready-with-known-limitations",
    "categories": [
        {
            "name": name,
            "status": status,
            "evidencePath": evidence,
            "latestValidationCommand": command,
            "remainingRisk": risk,
            "ownerFollowUp": owner
        }
        for name, status, evidence, command, risk, owner in categories
    ]
}
with open(sys.argv[1], "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
PY

echo "Unity client readiness audit written: $output"
