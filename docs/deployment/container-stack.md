# Container Stack

This stack runs the public backend API with durable JSON storage enabled. It is the local production-like target for reference headless and future Unity client integration.


## Start

```bash
docker compose up --build
```

The API listens on `http://localhost:5000` and exposes `/health`.

## Storage

`docker-compose.yml` mounts the named volume `game-api-storage` at `/var/lib/card-game` and sets:

```text
Storage__RootPath=/var/lib/card-game
ASPNETCORE_ENVIRONMENT=Production
```

Optional Unity WebGL CORS configuration:

```text
Api__Cors__Enabled=true
Api__Cors__AllowedOrigins=http://localhost:5173,http://localhost:8080,http://127.0.0.1:8080
Api__Cors__AllowedMethods=GET,POST,OPTIONS
Api__Cors__AllowedHeaders=Content-Type,X-Session-Token
```

CORS is disabled by default and should use explicit trusted origins only.

This avoids the development in-memory store path for catalog snapshots, decks, ruleset profiles, API sessions, active match runtime state, acceptance reports, and match evidence artifact refs.

## Unity Contract Dry-Run

After publishing catalog/profile/deck data through the public storage endpoints, run:

```bash
dotnet run --project src/Game.ReferenceHeadlessClient -- http://localhost:5000 unity-dry-run-match deck-p1 deck-p2
```

The reference client uses only public HTTP endpoints:

- `POST /sessions`
- `POST /matches`
- `GET /matches/{matchId}/players/{playerId}/legal-actions`
- `POST /matches/{matchId}/commands`
- `GET /matches/{matchId}/events`
- `POST /matches/{matchId}/reconnect`
- `POST /matches/{matchId}/replay/verify`
- `POST /match-evidence-artifacts`
- `GET /matches/{matchId}/evidence-artifacts`

The output report includes terminal match state, replay verification, event ordering verification, reconnect hash verification, replay trace refs when present, and dry-run artifact refs.

## Readiness Evidence

The integration suite includes a reproducible public API readiness evidence pass:

```bash
dotnet test tests/Game.Integration.Tests/Game.Integration.Tests.csproj --configuration Release --filter ReadinessEvidenceRunTests
```

It runs security-abuse, restart-recovery, and load-smoke scenarios against the HTTP API with durable storage enabled, writes temporary JSON evidence payloads, computes canonical content hashes, and validates the resulting refs through `ProductionReadinessEvidenceReportBuilder`.

The load-smoke scenario is intentionally small enough for local and CI execution. It proves the evidence chain and API behavior, but it does not replace the full-scale L10 load target from `AcceptanceAlgorithm.md`.

## Provider Evidence

This stack does not start mock or fake provider gateways. Local structured, image, animation, audio, and moderation gateways must be installed and run separately before provider evidence can pass. Missing provider configuration remains a blocker, not passing evidence.
