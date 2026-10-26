# Contributing

Discuss substantial changes in an issue. Keep changes focused and add tests for behavior changes.

Run `dotnet restore`, `dotnet build --configuration Release`, `dotnet test --configuration Release`, and `bash scripts/ai/forbidden-api-scan.sh`.

Preserve deterministic replay, canonical serialization, RNG separation, and the mutation pipeline. Never commit credentials or personal data.
