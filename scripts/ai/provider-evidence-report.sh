#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
if ! command -v "$dotnet_cmd" >/dev/null 2>&1; then
  if [ -x "/c/Program Files/dotnet/dotnet.exe" ]; then
    dotnet_cmd="/c/Program Files/dotnet/dotnet.exe"
  elif [ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then
    dotnet_cmd="/mnt/c/Program Files/dotnet/dotnet.exe"
  else
    echo "dotnet command not found. Set DOTNET_CMD to the dotnet executable path." >&2
    exit 127
  fi
fi
project="$root/tests/Game.Generator.Tests/Game.Generator.Tests.csproj"
if [[ "$dotnet_cmd" == /mnt/* && "$project" == /mnt/[a-zA-Z]/* ]]; then
  drive="${project:5:1}"
  rest="${project:7}"
  project="${drive^^}:\\${rest//\//\\}"
elif [[ "$dotnet_cmd" == /c/* && "$project" == /c/* ]]; then
  rest="${project:3}"
  project="C:\\${rest//\//\\}"
fi

echo "Provider evidence report verification"
echo "This CI-safe command validates blocked/configured report construction without printing secrets."
"$dotnet_cmd" test "$project" \
  --configuration Release \
  --filter "ProviderEvidenceReportBuilderTests|ProductionCardGenerationPipelineTests|ProductionAcceptanceReportBuilderTests"
