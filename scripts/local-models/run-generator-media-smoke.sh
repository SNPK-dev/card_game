#!/usr/bin/env bash
set -euo pipefail

dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- \
  --mode production \
  --count 1 \
  --provider-timeout-seconds 30 \
  --asset-provider-timeout-seconds 120 \
  --run-timeout-seconds 360 \
  --max-provider-retries 0 \
  --run-name generator-media-smoke \
  --output-root artifacts/unity-test-card-pack/generator-media-smoke \
  "$@"
