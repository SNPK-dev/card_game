# deterministic CCG platform

Russian version: [README.ru.md](README.ru.md). Russian Windows quick start: [docs/user/quick-start.ru.md](docs/user/quick-start.ru.md).

This repository is the bootstrap foundation for a deterministic collectible card game platform with AI-assisted card and asset generation.

The core promise is controlled scale: cards can be proposed by AI, but gameplay truth comes from a deterministic engine, a constrained canonical DSL, validation, simulation gates, immutable catalog versions, and replay from command logs.

## Architecture

- `Game.Domain` owns deterministic gameplay contracts and bootstrap behavior.
- `Game.Application` orchestrates use cases without owning gameplay rules.
- `Game.Infrastructure` is reserved for adapters.
- `Game.Api` is the future presentation/network boundary.
- `docs/engine/` and `docs/generator/` define the architectural contracts.

`Game.Domain` must not depend on infrastructure, network, database, LLM, asset generation, wall-clock time, or ambient randomness.

## Build

Required SDK: .NET `10.0.100`, pinned in `global.json`.

```bash
dotnet restore
dotnet build --configuration Release
```

## Run the Local Server

The Unity client uses the authoritative API at `http://127.0.0.1:5000`. From the repository root, start it with durable local storage:

```powershell
dotnet run --project src/Game.Api --configuration Release --no-launch-profile --urls http://127.0.0.1:5000 --Storage:RootPath=artifacts/local-player-data
```

Card generation has two modes. **Local** uses Ollama and Stable Diffusion through the bundled local gateways; install and cache the models first as described in [local generation setup](docs/user/local-generation.md). **OpenAI Connector** reads `OPENAI_API_KEY` from the nearest `.env` (repository root in development, beside `PLAY.bat` in a release) and uses the Responses and Images APIs for schemas and artwork.

Keep the server terminal open. Verify it from another PowerShell terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:5000/health
```

For the packaged Windows release, run the included `PLAY.bat`, or start the components manually from the release directory:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
./server/Game.Api.exe --urls http://127.0.0.1:5000 --Storage:RootPath=./data
```

Then launch `./client/windows/CardGame.exe` in a second terminal. See [the Windows quick start](docs/user/quick-start.md) for the complete account-to-match flow and troubleshooting.

## Test

```bash
dotnet test --configuration Release
bash scripts/ai/forbidden-api-scan.sh
```

The domain smoke tests exercise replay determinism, canonical serialization, state hashing, derived event logs, RNG separation, and mutation pipeline use.

## OpenSpec


The initial retrospective baseline is `bootstrap-ai-development-workflow`.

## Determinism Constraints

- Replay source is the canonical command log.
- Event log is derived output only.
- Gameplay state changes through `IStateMutator`.
- Gameplay and presentation RNG streams are separate.
- Presentation metadata does not affect gameplay state hashes.
- LLM/network/database/filesystem access is not required for deterministic execution.

## License

Licensed under [Apache-2.0](LICENSE).
