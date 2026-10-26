#!/usr/bin/env bash
set -euo pipefail

dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- \
  --mode production \
  --count 1 \
  --skip-assets \
  --provider-timeout-seconds 20 \
  --run-timeout-seconds 120 \
  --max-provider-retries 0 \
  --max-generation-attempts-per-card 1 \
  --max-repair-attempts-per-card 1 \
  --run-name generator-smoke-1 \
  --output-root artifacts/unity-test-card-pack/generator-smoke-1 \
  "$@"
