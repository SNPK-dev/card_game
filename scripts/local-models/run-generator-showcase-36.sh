#!/usr/bin/env bash
set -euo pipefail

dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- \
  --mode production \
  --count 36 \
  --skip-assets \
  --provider-timeout-seconds 45 \
  --run-timeout-seconds 1800 \
  --max-provider-retries 0 \
  --max-generation-attempts-per-card 1 \
  --max-repair-attempts-per-card 1 \
  --run-name generator-quality-showcase-36-v1 \
  --output-root artifacts/unity-test-card-pack/20260707-generator-quality-showcase-36-v1 \
  "$@"
