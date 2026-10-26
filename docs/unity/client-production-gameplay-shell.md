# Unity Production Gameplay Shell

## Status

The Unity client now has a production-facing gameplay shell structure over the validated local live-smoke client. It remains placeholder-only: no production art, audio, matchmaking, accounts, shop, or local gameplay rules are included.

## Runtime UI Structure

Runtime UI code is organized under `Assets/CardGame/Runtime/UI/`:

- `Shell`: aggregate shell state and controller binding surface.
- `Cards`: card identity, card-back, deck count, placeholder, and interaction models.
- `Board`: public entity, board zone, hero, weapon, location, keyword, and resource models.
- `Status`: player/opponent/spectator, mode, command, connection, capability, backend health, replay, and error models.
- `Interaction`: legal action, command guard, target selection, and choice models.
- `Presentation`: playback status models for presentation queues.

The scene prefab exposes placeholder hierarchy nodes: `Bootstrap`, `Canvas`, `Board`, `Hand`, `Status`, `Interaction`, `Presentation`, and `Diagnostics`. The `Canvas` node also carries local playtest regions: `Header`, `OpponentArea`, `BoardArea`, `PlayerArea`, `HandArea`, `InteractionArea`, `PresentationArea`, `EventFeed`, and `Diagnostics`.

## Hidden Information

Card identities come only from the current server projection. Opponent hand cards render as card-back/count placeholders. Deck zones are count-only for every viewer. Spectator projections render hand/deck identities only if the backend explicitly exposes visible public card data. `DebugBoth` remains development-only and does not merge hidden state between projections.

## Server-Driven Actions

Command UI models wrap server-provided legal action templates. Commands are disabled while booting, reconnecting, submitting, resolving a choice, spectating, terminal, or faulted. Target selection highlights only backend-provided candidate refs. Choice resolution uses server-provided choice and option ids.

## Presentation Intents

Public events map to presentation intents without requiring final art/audio. Unknown events become generic fallback intents with diagnostics. Terminal and fault events interrupt the queue. Presentation models do not mutate `MatchStateView`.

## Validation

Use:

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
```

The full live smoke remains the regression gate for boot, seed, sessions, start, state fetch, legal actions, command submit, events, reconnect, replay, and hidden-information assertions.

## Limitations

The shell is structurally ready for production-facing iteration, but visuals are placeholder-only. Full standalone/WebGL player output remains environment-dependent. Final animation, audio, real assets, matchmaking, account UX, shop UX, and production hosting are future stages.

## Next Stage

The next stage should integrate real presentation playback assets and production art/audio in a separately approved pass without changing gameplay authority boundaries.
