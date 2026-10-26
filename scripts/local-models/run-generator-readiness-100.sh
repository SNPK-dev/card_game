#!/usr/bin/env bash
set -euo pipefail

dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- \
  --mode production \
  --count 100 \
  --skip-assets \
  --provider-timeout-seconds 60 \
  --run-timeout-seconds 7200 \
  --max-provider-retries 0 \
  --max-generation-attempts-per-card 1 \
  --max-repair-attempts-per-card 1 \
  --run-name generator-readiness-100 \
  --output-root artifacts/unity-test-card-pack/20260707-production-generator-bounded-100-v24 \
  "$@"
