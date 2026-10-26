# External Match API

This API is the reference public gameplay contour for headless and future Unity clients. It does not expose engine internals and does not allow generation providers during a match.

Current Unity integration package:

- entry point: `docs/unity/README.md`;
- primary guide: `docs/unity/integration-guide.md`;
- machine-readable contract: `docs/api/openapi.yaml`;
- example fixtures: `docs/unity/examples/`;
- one-command local smoke: `scripts/unity/run-local-smoke.sh`;
- two-bot public API smoke: `scripts/unity/run-bot-match-smoke.sh`.


Unity clients must integrate through the public HTTP endpoints in this document. They must not reference `Game.Domain`, `Game.Application`, storage implementations, generator internals, or replay internals directly.

## Session

- `POST /sessions`
  - request: `playerId`, optional `sessionToken`
  - response: session token, player id, created/expiry timestamps
- Clients pass `X-Session-Token` for read/reconnect/event/replay endpoints.
- Command submission also carries `sessionToken` in the request body so command authorization is captured with the submitted command boundary.

## Match Lifecycle

- `POST /matches`
  - request: `matchId`, `gameId`, `deckIds`, `playerSessionTokens`, optional `startingPlayerId`, optional `spectatorSessionTokens`
  - requires one valid session token per deck owner
  - spectator tokens must be valid non-player sessions
  - starts from the existing readiness/match-start services
- `GET /matches/{matchId}/state`
  - returns a Unity-safe player or spectator state projection, state hash, active player, terminal state, command count, and next event sequence
  - player views expose only the caller's private hand; opponent and spectator hidden zones expose counts only
- `GET /matches/{matchId}/players/{playerId}/legal-actions`
  - returns legal command DTOs derived by `LegalActionOracle`
  - rejects sessions that do not own `playerId`
- `POST /matches/{matchId}/commands`
  - admits the command through `CommandAdmissionService` with the active state, catalog, ruleset profile, and runtime capability registry
  - execution uses `DeterministicCommandExecutor`; state changes only through the mutation pipeline
- `GET /matches/{matchId}/events?afterEventSeq=N`
  - polls derived gameplay events after the requested sequence
  - event category can be `PROPOSED`, `RESOLVER`, or `COMMITTED`; for example preventable damage with active prevention/replacement candidates emits `DAMAGE_WOULD_BE_DEALT` before `REPLACEMENT_RESOLVED` and any committed damage or prevention-consumption event
- `POST /matches/{matchId}/reconnect?afterEventSeq=N`
  - returns current state plus missed events
- `POST /matches/{matchId}/replay/verify`
  - verifies the stored command log against the stored initial state and current final state hash

## Authorization

- Player sessions can read match state, poll events, reconnect, verify replay, request legal actions for their own player id, and submit commands for their own player id.
- Spectator sessions can read match state, poll events, reconnect, and verify replay.
- Spectator sessions cannot request player legal actions and cannot submit gameplay commands.
- Player tokens cannot also be registered as spectator tokens for the same match.
- Expired session tokens are rejected at match start and at every match read/action/replay boundary.
- API runtime session time is sourced from the injected `TimeProvider` used by `MatchRuntimeService`; production DI uses `TimeProvider.System`.

## Request Validation

- Malformed JSON requests are rejected by the HTTP boundary with `400 Bad Request`.
- Requests with `Content-Length` above `Api:MaxRequestBodyBytes` are rejected before endpoint handlers execute with `413 Payload Too Large`.
- The default request body limit is `1048576` bytes.
- Mutating `POST` endpoints use the `api-write` fixed-window rate-limit policy.
- The default write limit is 60 requests per 60 seconds per remote address; configure it with `Api:WriteRateLimit:PermitLimit` and `Api:WriteRateLimit:WindowSeconds`.
- Rate-limit rejections return `429 Too Many Requests` with `RATE_LIMIT_EXCEEDED`.
- Unauthorized match reads, legal-action requests, event polls, reconnects, replay verification, and forbidden command submissions return stable HTTP `403 Forbidden` without requiring an ASP.NET authentication middleware challenge.

## Runtime Persistence

- Development mode uses in-memory session and match runtime stores when `Storage:RootPath` is not configured.
- Production storage mode uses `Storage:RootPath` to persist API sessions and match runtime records to JSON files.
- Persisted runtime records include initial state, current state, player-session ownership, spectator-session permissions, card versions, resolved profile, command log, event log, and next event sequence.
- After API process restart, clients can reuse valid session tokens to fetch state, poll missed events, reconnect, and verify replay from the persisted command log.

## Observability

- The API publishes `System.Diagnostics.Metrics` counters under meter `Game.Api`.
- `game.api.requests.rejected` records request-size and rate-limit rejections with reason, path, and status-code tags.
- `game.api.authorization.denied` records forbidden match reads/actions with endpoint and match id tags.
- `game.api.commands.admitted` records command admission outcomes with result, match id, player id, command type, and reason tags.
- The same outcomes are emitted through structured `ILogger` events.

## Reference Client

`src/Game.ReferenceHeadlessClient` is a public-API-only client. It logs in two players and one spectator, starts a match with two supplied decks, repeatedly reads legal actions, submits commands, polls events, reconnects, verifies replay, and stores dry-run match evidence refs through `/match-evidence-artifacts`.

Example:

```bash
dotnet run --project src/Game.ReferenceHeadlessClient -- http://localhost:5000 match-001 deck-p1 deck-p2
```

The JSON report contains:

- `isTerminal`, `terminalReason`, `stateHash`, and `commandCount`
- submitted command types
- polled event count, next event sequence, and `eventSequenceVerified`
- reconnect next event sequence and `reconnectStateHashMatches`
- `replayVerified`, replay final hash, derived event count, RNG trace ref, and debug trace ref
- stored dry-run `artifactRefs` and the refs listed back by `/matches/{matchId}/evidence-artifacts`

The integration test `ReferenceHeadlessClientDryRunTests.ReferenceHeadlessClientCompletesUnityContractDryRunThroughPublicApi` runs this same client runner against the HTTP API surface after publishing catalog/profile/deck fixtures through public storage endpoints.

## Unity Integration Checklist

The baseline covers these Unity-facing contracts:

- auth/session: `POST /sessions`, `X-Session-Token`, session expiry and spectator restrictions;
- match start: `POST /matches` with player sessions, deck ids, optional starting player, and optional spectator sessions;
- match state: `GET /matches/{matchId}/state` with public state view, state hash, active player, terminal state, command count, and next event sequence;
- legal actions: `GET /matches/{matchId}/players/{playerId}/legal-actions`, derived by `LegalActionOracle` and scoped to the owning player session;
- command submission: `POST /matches/{matchId}/commands`, authorized by session token and admitted before deterministic execution;
- events: `GET /matches/{matchId}/events?afterEventSeq=N`, ordered by event sequence and categorized as `PROPOSED`, `RESOLVER`, or `COMMITTED`;
- reconnect: `POST /matches/{matchId}/reconnect?afterEventSeq=N`, returning current state plus missed events;
- replay: `POST /matches/{matchId}/replay/verify`, verifying the stored canonical command log against state hashes;
- asset refs: asset manifests and match evidence refs are stable data refs; presentation assets do not affect gameplay state hash;
- presentation/playback: presentation timeline and playback plan contracts are backend-derived instructions for the future client runtime.

The reference client is the executable dry-run for this checklist. If Unity needs behavior not available through these public endpoints, that is a backend contract gap and must be handled through a new OpenSpec change rather than by reading backend internals.

## Readiness Evidence Runs

`ReadinessEvidenceRunTests.PublicApiEvidenceRunsProduceReadinessArtifactReport` performs a CI-safe public API evidence pass:

- security abuse checks for cross-player legal actions, spectator legal actions, spectator command submission, and stale pre-state hash rejection;
- restart recovery through a second API factory pointed at the same durable `Storage:RootPath`, followed by state and replay verification;
- a load smoke run that completes the reference headless dry-run and issues repeated health requests.

The test writes security, recovery, and load artifact payloads under a temporary `artifacts/` tree, computes canonical `sha256:<hex>` content hashes, and builds a `ProductionReadinessEvidenceReport` from those artifact refs. This is reproducible evidence wiring for L9/L10, not a replacement for the final full-scale load profile in `AcceptanceAlgorithm.md`.

## Container Stack

`docker-compose.yml` starts `Game.Api` with `ASPNETCORE_ENVIRONMENT=Production` and `Storage__RootPath=/var/lib/card-game`, backed by the `game-api-storage` volume. See `docs/deployment/container-stack.md`.

The stack intentionally does not start mock provider gateways. Real local provider gateways remain external prerequisites for provider evidence.

## Deferred Work After Unity Baseline

- Full-scale load evidence for the `AcceptanceAlgorithm.md` production profile remains outstanding; the current automated run is a CI-safe smoke/evidence-chain check.
- Broader future mechanics, full production hardening, and host-client media playback are deferred to later tracks and do not block starting the Unity Client Track.
