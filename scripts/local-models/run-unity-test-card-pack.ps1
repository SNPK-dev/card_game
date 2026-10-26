param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Args
)

$ErrorActionPreference = "Stop"

dotnet run `
  --project src/Game.UnityCardPackExporter/Game.UnityCardPackExporter.csproj `
  --configuration Release `
  -- $Args
