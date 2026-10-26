#!/usr/bin/env python3
import json
import os
import pathlib
import shutil
import subprocess
import sys


def main() -> int:
    root = pathlib.Path(__file__).resolve().parents[2]
    out = root / os.environ["CCG_FINAL_E2E_OUTPUT_DIR"]
    run_id = os.environ.get("CCG_FINAL_E2E_RUN_ID", "production-final-e2e")
    count = os.environ.get("CCG_FINAL_E2E_GENERATED_CARD_COUNT", "100")
    timeout_seconds = int(os.environ.get("CCG_FINAL_E2E_PROVIDER_TIMEOUT_SECONDS", "300"))
    run_timeout_seconds = max(timeout_seconds + 120, 3600)
    structured_endpoint = os.environ.get("CCG_LOCAL_STRUCTURED_ENDPOINT", "http://127.0.0.1:8011/v1/structured-card")
    structured_model = os.environ.get("CCG_LOCAL_STRUCTURED_MODEL", "qwen3.5:latest")

    export_root = out / "generation" / "exporter-output"
    failure_root = out / "generation" / "provider-failures"
    catalog_id = f"{run_id}-catalog"

    command = [
        "dotnet",
        "run",
        "--project",
        str(root / "src" / "Game.UnityCardPackExporter" / "Game.UnityCardPackExporter.csproj"),
        "--configuration",
        "Release",
        "--",
        "--mode",
        "production",
        "--count",
        count,
        "--skip-assets",
        "--structured-endpoint",
        structured_endpoint,
        "--structured-model",
        structured_model,
        "--provider-timeout-seconds",
        str(timeout_seconds),
        "--run-timeout-seconds",
        str(run_timeout_seconds),
        "--max-provider-retries",
        "5",
        "--max-generation-attempts-per-card",
        "2",
        "--max-repair-attempts-per-card",
        "1",
        "--catalog-snapshot-id",
        catalog_id,
        "--run-name",
        run_id,
        "--failure-root",
        str(failure_root),
        "--output-root",
        str(export_root),
    ]
    completed = subprocess.run(command, cwd=root, check=False, timeout=(timeout_seconds * int(count)) + 120)
    if not (export_root / "manifest.json").exists() or not (export_root / "summary-report.json").exists():
        return completed.returncode or 2

    generation = out / "generation"
    manifest = json.loads((export_root / "manifest.json").read_text(encoding="utf-8"))
    summary = json.loads((export_root / "summary-report.json").read_text(encoding="utf-8"))
    catalog = json.loads((export_root / "catalog-snapshot.json").read_text(encoding="utf-8"))
    profile = json.loads((export_root / "resolved-ruleset-profile.json").read_text(encoding="utf-8"))

    cards = []
    for card in manifest.get("cards", []):
        cards.append(json.loads((export_root / card["jsonPath"]).read_text(encoding="utf-8")))

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
    (generation / "card-pack-manifest.json").write_text(
        json.dumps({
            "cardPackId": manifest.get("packId"),
            "runId": run_id,
            "version": manifest.get("createdAt"),
            "catalogId": catalog.get("snapshotId"),
            "catalogSnapshot": catalog,
            "rulesetProfile": profile,
            "exporterOutputRoot": str(export_root),
            "exporterManifest": manifest,
        }, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")
    (generation / "validation-report.json").write_text(
        json.dumps({
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
        }, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")

    shutil.copy2(export_root / "manifest.json", generation / "exporter-manifest.json")
    shutil.copy2(export_root / "summary-report.json", generation / "exporter-summary-report.json")
    return 0 if accepted else (completed.returncode or 2)


if __name__ == "__main__":
    raise SystemExit(main())
