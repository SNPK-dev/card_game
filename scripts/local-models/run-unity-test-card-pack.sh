#!/usr/bin/env bash
set -euo pipefail

dotnet run \
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj \
  --configuration Release \
  -- "$@"
