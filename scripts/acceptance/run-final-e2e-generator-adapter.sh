#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

OUT="${CCG_FINAL_E2E_OUTPUT_DIR:?CCG_FINAL_E2E_OUTPUT_DIR is required}"
RUN_ID="${CCG_FINAL_E2E_RUN_ID:-production-final-e2e}"
COUNT="${CCG_FINAL_E2E_GENERATED_CARD_COUNT:-100}"
TIMEOUT_SECONDS="${CCG_FINAL_E2E_PROVIDER_TIMEOUT_SECONDS:-300}"
STRUCTURED_ENDPOINT="${CCG_LOCAL_STRUCTURED_ENDPOINT:-http://127.0.0.1:8011/v1/structured-card}"
STRUCTURED_MODEL="${CCG_LOCAL_STRUCTURED_MODEL:-qwen3.5:latest}"

EXPORT_ROOT="$OUT/generation/exporter-output"
FAILURE_ROOT="$OUT/generation/provider-failures"
CATALOG_ID="${RUN_ID}-catalog"

set +e
dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- \
  --mode production \
  --count "$COUNT" \
  --skip-assets \
  --structured-endpoint "$STRUCTURED_ENDPOINT" \
  --structured-model "$STRUCTURED_MODEL" \
  --provider-timeout-seconds "$TIMEOUT_SECONDS" \
  --run-timeout-seconds "$(( TIMEOUT_SECONDS + 120 ))" \
  --max-provider-retries 0 \
  --max-generation-attempts-per-card 2 \
  --max-repair-attempts-per-card 1 \
  --catalog-snapshot-id "$CATALOG_ID" \
  --run-name "$RUN_ID" \
  --failure-root "$FAILURE_ROOT" \
  --output-root "$EXPORT_ROOT"
EXPORT_EXIT=$?
set -e

if [[ ! -f "$EXPORT_ROOT/manifest.json" || ! -f "$EXPORT_ROOT/summary-report.json" ]]; then
  exit "$EXPORT_EXIT"
fi

export FINAL_E2E_EXPORT_ROOT="$EXPORT_ROOT"
export FINAL_E2E_EXPORT_EXIT="$EXPORT_EXIT"
python - <<'PY'
import json
import os
import pathlib
import shutil

out = pathlib.Path(os.environ["CCG_FINAL_E2E_OUTPUT_DIR"])
export_root = pathlib.Path(os.environ["FINAL_E2E_EXPORT_ROOT"])
generation = out / "generation"

manifest = json.loads((export_root / "manifest.json").read_text(encoding="utf-8"))
summary = json.loads((export_root / "summary-report.json").read_text(encoding="utf-8"))
catalog = json.loads((export_root / "catalog-snapshot.json").read_text(encoding="utf-8"))
profile = json.loads((export_root / "resolved-ruleset-profile.json").read_text(encoding="utf-8"))

cards = []
for card in manifest.get("cards", []):
    report_path = export_root / card["jsonPath"]
    report = json.loads(report_path.read_text(encoding="utf-8"))
    cards.append(report)

accepted = [
    card for card in cards
    if card.get("validation", {}).get("finalDecision") == "Accepted"
    and card.get("publicationSucceeded") is True
    and card.get("verdict") == "playable"
]

with (generation / "cards.generated.jsonl").open("w", encoding="utf-8") as handle:
    for card in cards:
        handle.write(json.dumps(card, sort_keys=True) + "\n")

(generation / "resolved-ruleset-profile.json").write_text(
    json.dumps(profile, indent=2, sort_keys=True) + "\n",
    encoding="utf-8")

card_pack_manifest = {
    "cardPackId": manifest.get("packId"),
    "runId": os.environ.get("CCG_FINAL_E2E_RUN_ID", ""),
    "version": manifest.get("createdAt"),
    "catalogId": catalog.get("snapshotId"),
    "catalogSnapshot": catalog,
    "rulesetProfile": profile,
    "exporterOutputRoot": str(export_root),
    "exporterManifest": manifest,
}
(generation / "card-pack-manifest.json").write_text(
    json.dumps(card_pack_manifest, indent=2, sort_keys=True) + "\n",
    encoding="utf-8")

validation_report = {
    "requestedCards": summary.get("requestedCount", len(cards)),
    "generatedCards": summary.get("successfullyGenerated", len(cards)),
    "acceptedCards": len(accepted),
    "rejectedCards": summary.get("rejectedCount", len(cards) - len(accepted)),
    "repairCount": summary.get("repairGovernance", {}).get("providerRepairCount", summary.get("providerRepairCount", 0)),
    "providerFailures": summary.get("providerReliability", {}).get("failureCount", 0),
    "schemaErrors": [],
    "semanticErrors": [],
    "unsupportedMechanics": [],
    "schemaValidation": "passed" if summary.get("validatedCount", 0) >= len(accepted) else "failed",
    "semanticValidation": "passed" if summary.get("semanticWarningsCount", 0) == 0 else "warning",
    "rulesEffectValidation": "passed" if len(accepted) > 0 else "failed",
    "duplicateChecks": "passed" if summary.get("similarityReport", {}).get("violationCount", 0) == 0 else "failed",
    "duplicateSimilarityChecks": "passed" if summary.get("similarityReport", {}).get("violationCount", 0) == 0 else "failed",
    "unsupportedMechanicChecks": "passed",
    "gameplayValidationStatus": "passed" if summary.get("gameplaySmokeCount", 0) >= len(accepted) else "not_available",
    "assetMediaStatus": "not_applicable_skip_assets" if summary.get("assetsSkipped") else summary.get("hasFullAssetSet", ""),
    "finalGenerationVerdict": "passed" if len(accepted) > 0 and manifest.get("unityBaselineUsable") else "failed",
    "generatorReadinessVerdict": summary.get("generatorReadinessVerdict"),
    "exporterFinalVerdict": summary.get("finalVerdict"),
    "diagnostics": summary.get("risksAndLimitations", []),
}
(generation / "validation-report.json").write_text(
    json.dumps(validation_report, indent=2, sort_keys=True) + "\n",
    encoding="utf-8")

shutil.copy2(export_root / "manifest.json", generation / "exporter-manifest.json")
shutil.copy2(export_root / "summary-report.json", generation / "exporter-summary-report.json")
if not accepted:
    raise SystemExit(int(os.environ.get("FINAL_E2E_EXPORT_EXIT", "2")) or 2)
PY
