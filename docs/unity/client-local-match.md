# Unity Local Match Client

## Start The Backend

From the repository root:

```bash
dotnet run --project src/Game.Api
```

Optional pre-checks:

```bash
bash scripts/unity/run-local-smoke.sh
bash scripts/unity/run-bot-match-smoke.sh
bash scripts/unity/run-webgl-cors-smoke.sh
```

The default Unity backend URL is `http://localhost:5000`.

## Open The Unity Scene

Open `clients/unity/CardGame.Unity` and load `Assets/Scenes/CardGameLocalMatch.unity`.

The scene contains `CardGameClientRoot` with `DebugMatchBootstrapper`. In another development scene, use `Card Game > Create Debug Client Root`. The debug client is opt-in and development-scoped.

`CardGameClientRoot` exposes production-facing placeholder hierarchy nodes for `Bootstrap`, `Canvas`, `Board`, `Hand`, `Status`, `Interaction`, `Presentation`, and `Diagnostics`. `Canvas` includes `Header`, `OpponentArea`, `BoardArea`, `PlayerArea`, `HandArea`, `InteractionArea`, `PresentationArea`, `EventFeed`, and `Diagnostics` regions. These nodes are structure only; final art/audio remains out of scope.

## Seed And Start

In Play Mode, `Boot` checks `/health` and `/engine/capabilities`, `Seed` ensures deterministic local data through public endpoints, and `Start` creates player and spectator sessions, starts a collision-safe match id, fetches state/actions, and polls initial events.

## Modes

- `PlayerOne`: player 1 projection with command submission.
- `PlayerTwo`: player 2 projection with command submission.
- `Spectator`: spectator projection, no command buttons.
- `DebugBoth`: local developer-only projection switching without hidden-data merging.

## Target Selection

Unity submits only server-provided legal action templates. Complete target templates submit directly. Target-like templates with server-provided candidate refs enter target mode and highlight only those refs. Unsupported target actions are disabled with `Target selection unavailable: backend did not provide target candidates.`

## Choices

Visible choices render from the current projection with choice type, source, result zone, option id, and option visibility. Missing option ids are disabled. `RESOLVE_CHOICE` uses server-provided `choiceId` and `selectedOptionId`, then refreshes state/legal actions.

## Event Visual Queue

Accepted public events are deduped by `eventSeq`, queued in sequence order, and displayed one at a time. Terminal and fault events interrupt pending visuals. Unknown events use fallback timing and request state refresh. The visual layer never mutates `MatchStateView`.

## Reconnect And Replay

Reconnect runs after stale command hash, polling transport failure, and app focus/resume when a match is active. Commands are disabled while reconnecting. Reconnect replaces state from the server projection, appends missed events once, and refreshes legal actions. Replay verification remains canonical-command-log based.

## Smoke Status

`Assets/CardGame/Tests/PlayMode/UnityClientLiveBackendSmokeTests.cs` skips unless `CARDGAME_UNITY_LIVE_BACKEND_TESTS=1`. When enabled, it runs the full Unity-side local backend flow through `LocalMatchFlowDriver`: boot, seed, sessions, match start, state projections, legal action, command, event poll, reconnect, and replay verification. The 2026-07-08 enabled batchmode run passed with verdict `unity-client-full-live-match-smoke-ready`; sanitized evidence is committed at `docs/unity/examples/unity-live-smoke-report.actual-sanitized.json`.

`Card Game > Validate Local Match Build Smoke` validates the local match scene/root. `scripts/unity/run-unity-dev-build-smoke.sh --validate-only` can run it in batchmode when `UNITY_EDITOR` is set.

## Limitations

No production art/audio, imported assets, matchmaking, accounts, shop, local gameplay rules, or generator work. Target candidate support depends on backend-provided legal action metadata.

## Next Stage

Use the usable gameplay UI and presentation feed for repeated local playtests, then add final presentation assets in a later stage.
