# Unity Integration Guide

## 1. Purpose

This is the primary Unity-facing integration document for the deterministic CCG backend. It defines the public HTTP API, DTO expectations, match flow, hidden-information policy, local smoke flow, and client architecture baseline needed before building the Unity client.

Authoritative machine-readable contract: `docs/api/openapi.yaml`.

Example fixtures: `docs/unity/examples/`.

Executable public API proof: `src/Game.ReferenceHeadlessClient` and `scripts/unity/run-local-smoke.sh`.

## 2. What Unity Should And Should Not Depend On

Unity should depend on:

- public HTTP endpoints only;
- DTO fields documented in `docs/api/openapi.yaml`;
- JSON examples under `docs/unity/examples/`;
- server-derived legal actions;
- server state hashes, command counts, and event sequence numbers;
- asset manifests and presentation playback plans as rendering inputs.

Unity must not depend on:

- `Game.Domain`, `Game.Application`, storage classes, generator classes, or replay internals;
- event logs as a replay source;
- local rule implementation for action legality;
- opponent private card identities, deck order, secret internals, hidden choices, or RNG state;
- generated media availability during match execution.

The engine remains the source of gameplay truth. Unity renders state, legal actions, and events; it does not execute rules locally.

## 3. Local Backend Startup

Fast local API:

```bash
dotnet run --project src/Game.Api --urls http://localhost:5000
```

Production-like local stack with durable JSON storage:

```bash
docker compose up --build
```

Known port: `http://localhost:5000`.

Health check:

```bash
curl http://localhost:5000/health
```

Durable storage is enabled when `Storage__RootPath` is set. Docker Compose stores runtime data in the `game-api-storage` volume at `/var/lib/card-game`.

Reset local Docker data:

```bash
docker compose down -v
```

CORS is configurable for Unity WebGL/browser-hosted calls. See `webgl.md` and run `bash scripts/unity/run-webgl-cors-smoke.sh`.

## 4. Required Boot Sequence

1. Call `GET /health`.
2. Call `GET /engine/capabilities`.
3. Verify the backend supports the Unity contract version, asset manifest version, state schema, event schema, and required command types.
4. Load or publish catalog/profile/deck data through the public storage endpoints.
5. Create sessions with `POST /sessions`.
6. Start a match with `POST /matches`.
7. Each player fetches its own projected state with `GET /matches/{matchId}/state` and `X-Session-Token`.
8. Render legal actions from `GET /matches/{matchId}/players/{playerId}/legal-actions`.

## 5. API Overview

Unity-facing endpoints:

- `GET /health`
- `GET /engine/capabilities`
- `POST /sessions`
- `POST /matches`
- `GET /matches/{matchId}/state`
- `GET /matches/{matchId}/players/{playerId}/legal-actions`
- `POST /matches/{matchId}/commands`
- `GET /matches/{matchId}/events?afterEventSeq=N`
- `POST /matches/{matchId}/reconnect?afterEventSeq=N`
- `POST /matches/{matchId}/replay/verify`
- `GET /assets/manifests/current`
- `GET /assets/manifests/{assetManifestId}`
- `GET /assets/manifests/{assetManifestId}/compatibility`
- `GET /assets/manifests/{assetManifestId}/presentation-hash`
- `POST /presentation/timelines`
- `POST /presentation/playback-plans`
- `POST /presentation/playback-instructions`

Session header for reads/reconnect/replay:

```text
X-Session-Token: <session token>
```

Command submission also includes `sessionToken` in the JSON body.

## 6. Auth And Session Flow

Create a player or spectator session:

```http
POST /sessions
Content-Type: application/json
```

```json
{
  "playerId": "player-1",
  "sessionToken": "unity-local-session-p1"
}
```

The response contains `session.sessionToken`, `session.playerId`, `createdAt`, and `expiresAt`.

Player sessions can read state, poll events, reconnect, verify replay, request their own legal actions, and submit their own commands. Spectator sessions can read state, poll events, reconnect, and verify replay. Spectators cannot request player legal actions or submit commands.

Expired sessions are rejected at match start and runtime boundaries.

## 7. Match Lifecycle Flow

Start a match:

```http
POST /matches
Content-Type: application/json
```

```json
{
  "matchId": "unity-local-match",
  "gameId": "unity-local-game",
  "deckIds": ["deck-p1", "deck-p2"],
  "playerSessionTokens": {
    "player-1": "unity-local-session-p1",
    "player-2": "unity-local-session-p2"
  },
  "startingPlayerId": "player-1",
  "spectatorSessionTokens": ["unity-local-session-s1"]
}
```

The match start response includes a spectator-safe state snapshot. Each Unity player should immediately fetch its own player view with `GET /matches/{matchId}/state`.

Terminal state is represented by `isTerminal=true` and `terminalReason`.

## 8. State And View Model

`MatchStateView` includes:

- `matchId`
- `viewKind`: `PLAYER` or `SPECTATOR`
- `viewerPlayerId`: player id for player views, `null` for spectator
- `revision`
- `stateHash`
- `activePlayerId`
- `turnNumber`
- `phase`
- `isTerminal`
- `terminalReason`
- `players`
- `pendingChoices`
- `nextEventSeq`
- `commandCount`

Visibility rules:

- Self hand cards are visible in a player view.
- Self deck order is not exposed; deck zone returns count with empty `cards`.
- Opponent hand, deck, secret-zone, and set-aside cards return counts only.
- Opponent unrevealed secrets do not expose source card instance, card version, reveal event, or effect id.
- Spectator views do not expose any player hand, deck, secret-zone, or set-aside card identities.
- Graveyards and board entities are public.
- Pending choices are visible to the owning player unless the choice visibility policy is public.
- Private RNG state is never exposed.

The state hash is the authoritative gameplay hash. Redacted transport fields do not change gameplay state or replay verification.

## 9. Legal Actions

Unity renders allowed actions from:

```http
GET /matches/{matchId}/players/{playerId}/legal-actions
X-Session-Token: <player session token>
```

Response fields:

- `succeeded`
- `legalActions`
- `legalDiagnostics`
- `diagnostics`

Each legal action is a command template:

- `commandType`
- `playerId`
- `payload`
- `refs`

Unity should not infer legality. If an action does not appear, disable or hide that UI action. If future disabled reasons are surfaced in `legalDiagnostics`, map them into non-blocking UI hints.

## 10. Commands

Submit commands:

```http
POST /matches/{matchId}/commands
Content-Type: application/json
```

```json
{
  "sessionToken": "unity-local-session-p1",
  "command": {
    "commandType": "PLAY_CARD",
    "playerId": "player-1",
    "payload": { "cost": 1, "attack": 1, "health": 1 },
    "refs": {
      "cardInstanceId": "player-1:card_public_minion:001",
      "entityInstanceId": "entity:player-1:card_public_minion:001",
      "heroPowerActivationTiming": ""
    }
  },
  "commandId": "unity-command-0001",
  "expectedPreStateHash": "<last stateHash>",
  "correlationId": "unity-ui-action-0001"
}
```

Supported public Unity commands:

| Command | Appears When | Required Refs/Payload | Unity UI Implication |
| --- | --- | --- | --- |
| `PLAY_CARD` | A hand card is playable by server rules | `cardInstanceId`, `entityInstanceId`; payload can include cost/stat/target values from legal action | Render playable card affordance from legal action only |
| `ATTACK` | A legal attacker/target pair exists | `attackerEntityInstanceId`, `targetPlayerId`, `targetEntityInstanceId` | Render target selection from legal action template |
| `END_TURN` | Usually active player can pass priority/turn | no refs required | Always use server action, not client timer rules |
| `CONCEDE` | Player may concede | no refs required | Confirm in UI before submit |
| `ACTIVATE_LOCATION` | Location can activate | `locationEntityInstanceId`, `sourceCardInstanceId`, target refs as provided | Render location activation button only when present |
| `USE_HERO_POWER` | Hero power is available | `sourceCardInstanceId`, target refs as provided | Render hero power affordance only when present |
| `RESOLVE_CHOICE` | Pending choice exists for player | `choiceId`, `selectedOptionId` or `selectedOptionIds`; materialization refs when supplied | Drive choice UI from `pendingChoices` and legal action refs |

`expectedPreStateHash` should be the last state hash Unity accepted. If it is stale, the server rejects with `COMMAND_PRE_STATE_HASH_MISMATCH`; Unity should fetch state or reconnect, then rebuild the command from current legal actions.

`commandId` and `correlationId` are transport metadata. They are excluded from replay. Duplicate command idempotency is not currently a public guarantee; Unity should not retry a command blindly after a network timeout. Reconnect first and inspect `commandCount`, `stateHash`, and events.

## 11. Events And Polling

Poll events:

```http
GET /matches/{matchId}/events?afterEventSeq=0
X-Session-Token: <session token>
```

`afterEventSeq` is exclusive. A value of `0` returns all available events. Events are ordered by `eventSeq`, and `nextEventSeq` is the next sequence the server will assign.

Recommended polling baseline: 250-500 ms while a match scene is active, slower when backgrounded. A future WebSocket/SignalR layer may push event notifications, but HTTP state/reconnect/commands remain authoritative.

Unity event handling matrix:

| Event | Unity Baseline |
| --- | --- |
| `CARD_PLAYED` | Remove card from visible source if known, animate play |
| `CARD_MOVED_ZONE` | Update zone counters/cards according to current state refresh |
| `CARD_DRAWN` | Animate draw for visible owner; opponent gets count-only update |
| `ENTITY_DAMAGED` | Damage number and health update |
| `ENTITY_HEALED` | Heal number and health update |
| `ENTITY_DIED` / `ENTITY_DESTROYED` | Death/destruction animation, then state refresh |
| `DEATHRATTLE_TRIGGERED` | Trigger cue on source entity/card |
| `CHOICE_CREATED` | Open choice UI only for visible/owned choices |
| `CHOICE_RESOLVED` | Close choice UI and animate selected result if public |
| `AURA_REFRESHED` | Recompute board display from state |
| `WEAPON_EQUIPPED` | Attach weapon UI |
| `LOCATION_ACTIVATED` | Play location activation cue and refresh durability |
| `HERO_POWER_USED` / `HERO_POWER_REPLACED` | Update hero power slot |
| `SECRET_REVEALED` | Reveal secret if event visibility is public |
| `DAMAGE_WOULD_BE_DEALT` | Optional anticipation cue for replacement/prevention window |
| `REPLACEMENT_RESOLVED` | Show prevention/replacement outcome |
| `RNG_ROLLED` | Do not expose as gameplay source; use only for debug/replay evidence if surfaced |
| `GAME_ENDED` / `ENGINE_FAULTED` | End match scene and show terminal reason |

Unknown future events should be logged, ignored for bespoke animation, and followed by a state refresh.

## 12. Reconnect

Call reconnect after network drops, app resume, missed event suspicion, stale hash rejection, or a local state mismatch:

```http
POST /matches/{matchId}/reconnect?afterEventSeq=<last seen eventSeq>
X-Session-Token: <session token>
```

Response includes current caller-safe state, missed events, and `nextEventSeq`.

Reconnect handling:

- If local `stateHash` differs, replace local state with server state.
- Apply missed events only for animation catch-up, not as replay truth.
- If session is expired or forbidden, return to session creation.
- If match is terminal, show the terminal scene from returned state.

## 13. Replay Verification

Call:

```http
POST /matches/{matchId}/replay/verify
X-Session-Token: <session token>
```

Replay verifies the canonical command log against the initial state and final state hash. Unity should surface replay mismatch as a blocking integrity error and stop trusting local animation state.

## 14. Assets And Presentation

Asset manifests describe image, animation, and audio refs:

- `assetManifestId`
- `assetManifestVersion`
- `cardId`
- `cardVersionId`
- visual/audio mode
- visual/audio refs
- fallback policy
- moderation status
- content hash

Asset refs can be local pack paths or HTTP paths depending on publication. Missing assets should use placeholder art/audio or silence according to `fallbackPolicy`. Gameplay does not depend on assets, and asset/presentation hashes do not affect gameplay state hashes.

Presentation flow:

1. Build timeline with `POST /presentation/timelines` from events plus asset manifests.
2. Build playback plan with `POST /presentation/playback-plans`.
3. Build runtime instructions with `POST /presentation/playback-instructions`.

Playback instructions are client-rendering directives only.

Current generated media is integration baseline quality, not final polished client media.

## 15. Error Handling

Unity-facing error shape in docs/examples:

```json
{
  "code": "MATCH_SESSION_FORBIDDEN",
  "message": "The session token is missing, expired, or not authorized for this match/player.",
  "retryable": false,
  "suggestedUnityBehavior": "Return to session creation or reconnect with a valid token; do not resubmit the command."
}
```

Implemented HTTP behaviors:

| Case | Status | Code / Signal | Unity Behavior |
| --- | --- | --- | --- |
| Malformed JSON | 400 | HTTP bad request | Fix client serialization |
| Validation error | 400 | diagnostics code | Show non-retryable action error |
| Missing/expired/forbidden session | 403 | forbidden boundary or `MATCH_SESSION_FORBIDDEN` | Re-auth/reconnect |
| Match not found | 403 read boundary or diagnostic | `MATCH_NOT_FOUND` | Leave match scene |
| Illegal command | 400 | admission diagnostics | Refresh legal actions |
| Stale pre-state hash | 400 | `COMMAND_PRE_STATE_HASH_MISMATCH` | Reconnect, rebuild command |
| Rate limit | 429 | `RATE_LIMIT_EXCEEDED` | Back off |
| Request too large | 413 | `REQUEST_BODY_TOO_LARGE` | Reduce payload |
| Replay mismatch | 200 with replay not verified or diagnostic | replay diagnostics | Stop and report integrity failure |
| Unsupported command/client version | 400 capability/diagnostic | compatibility failure | Fail fast at boot |
| Server fault | 5xx or terminal `ENGINE_FAULTED` | server/terminal reason | Show blocking error |

## 16. Versioning And Capabilities

Unity should call `GET /engine/capabilities` at boot and validate:

- engine version;
- supported commands/actions/effects;
- supported asset manifest versions;
- catalog/profile compatibility;
- minimum Unity contract version from the OpenAPI version and release note.

Forward compatibility rules:

- Ignore unknown JSON fields.
- Treat unknown enum/string values as non-renderable but non-fatal unless they affect commands.
- Do not submit unsupported command types.
- Optional features should be gated by capability flags.

## 17. Unity Client Architecture Recommendation

Recommended Unity components:

- `BackendApiClient`: HTTP wrapper, JSON settings, headers, retries for reads only.
- `CapabilityChecker`: boot-time compatibility check.
- `SessionService`: session creation and token persistence.
- `MatchService`: start/read/replay calls.
- `MatchStateStore`: authoritative local projection of latest server state.
- `LegalActionPresenter`: maps legal actions to interactable UI.
- `CommandBuilder`: creates command requests only from legal action templates.
- `CommandQueue`: serializes command submission and blocks duplicates until state advances.
- `EventPoller`: polls events and hands them to animation systems.
- `ReconnectService`: state/hash/event recovery.
- `AssetManifestImporter`: loads refs, hashes, and fallbacks.
- `PresentationPlaybackController`: consumes timeline/playback instructions.
- `ErrorMapper`: maps status/code to UI behavior.

HTTP-only baseline is acceptable. Future push transport should push event availability only; HTTP remains source of truth for commands, state, and reconnect.

## 18. Minimal Scene Implementation Checklist

- Boot capability check.
- Create two player sessions and optional spectator session.
- Start match from supplied decks.
- Fetch player state.
- Render heroes, resources, board, hand, and opponent counters.
- Fetch legal actions for active player.
- Submit one legal action with expected pre-state hash.
- Poll events and play baseline animations.
- Reconnect and verify local state hash.
- Verify replay at terminal or debug checkpoint.
- Load asset manifest and apply fallback placeholders.

## 19. Smoke Test Checklist

One-command local smoke:

```bash
bash scripts/unity/run-local-smoke.sh
```

Manual equivalent:

1. Start backend or test host.
2. Check `/health`.
3. Publish demo catalog/profile/decks.
4. Create player and spectator sessions.
5. Start match.
6. Fetch player state.
7. Fetch legal actions.
8. Submit one legal command.
9. Poll events.
10. Reconnect.
11. Verify replay.

The script uses the public-API-only reference headless client integration test, which seeds demo data through public endpoints and exercises state, legal actions, commands, events, reconnect, replay, and evidence artifacts.

## 20. Known Limitations And Deferred Work

- Full Unity client is not implemented in this repository.
- WebSocket/SignalR event push is not implemented; HTTP polling is the baseline.
- Unity WebGL CORS is available through explicit configuration; see `webgl.md`.
- Generated media remains baseline integration quality, not final art/audio polish.
- Full-scale production load evidence from `AcceptanceAlgorithm.md` is outside this Unity integration contract pass.
- Broader future mechanics remain governed by OpenSpec and capability/profile compatibility.
