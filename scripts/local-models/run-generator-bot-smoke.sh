#!/usr/bin/env bash
set -euo pipefail

dotnet test tests/Game.Integration.Tests/Game.Integration.Tests.csproj \
  --configuration Release \
  --filter GeneratedDeckBotVsBotE2ETests
