# Unity Usable Gameplay UI And Presentation

## Status

The local match scene now has a usable built-in Unity UI for repeated development playtests. It remains placeholder-only and uses no production art, audio, external assets, matchmaking, accounts, shop, local gameplay rules, generator work, backend internals, or backend contract changes.

## Start Backend And Scene

Start the backend from the repository root:

```bash
dotnet run --project src/Game.Api --configuration Release
```

Open `clients/unity/CardGame.Unity`, then load `Assets/Scenes/CardGameLocalMatch.unity`. The scene root is `CardGameClientRoot`.

## Use The Screen

Use the top controls in order:

1. `Boot` checks `/health` and `/engine/capabilities`.
2. `Seed` ensures deterministic demo data through public endpoints. Compatible existing data is accepted; incompatible data fails with diagnostics.
3. `Start` creates player and spectator sessions, starts a local match, fetches state, legal actions, and initial events.

The gameplay screen shows header, opponent, board, player, hand, interaction, presentation, event feed, and diagnostics regions.

The placeholder UI uses larger labels, explicit face-up/card-back markers, target banners, disabled reasons, connection/replay state, and separated diagnostics. No external assets are required.

## Modes

- `PlayerOne` and `PlayerTwo` render the selected player projection and can submit commands when legal.
- `Spectator` renders the spectator projection, disables command submission, and keeps presentation/event/reconnect/replay visible.
- `DebugBoth` is development-only. Projection switching replaces state and cancels target selection; it does not merge hidden identity data.

## Cards, Hands, And Hidden Information

Own visible hand cards show backend-projected identity. Opponent and spectator hidden hands render count/card-back placeholders. Decks render count only. Highlighting comes from server legal action refs, not local legality inference.

## Board And Target Selection

Board entities are rendered in deterministic ref order. Target selection highlights only backend-provided candidate refs from legal action templates. Clicking a non-candidate produces a diagnostic and does not submit a command.

## Legal Actions And Choices

Legal action buttons bind server templates and expected pre-state hashes. Stale hashes force reconnect and fresh legal actions before resubmission. Visible choices show choice id, type, source, result zone, option labels, and option visibility. Options without ids are disabled. Resolving a choice uses server-provided `choiceId` and `selectedOptionId`.

## Presentation Playback

Accepted command, poll, and reconnect events enter a presentation intent queue. The presentation area shows the current intent and feed. Unknown events use fallback diagnostics. Terminal/fault events block normal playback and remain visible. Presentation does not mutate `MatchStateView`.

Presentation intents now carry deterministic durations and placeholder semantics: card movement, combat/damage, turn changed, choice opened/resolved, terminal banner, fault banner, reconnect status, and replay status.

## Reconnect And Replay

Reconnect is available through the UI and remains automatic on stale hash, polling failure, and focus resume. Diagnostics show reconnect status and missed event counts. Replay verify is visible; mismatch is blocking and disables commands.

## Playtest Evidence

Use `Export` to write local playtest evidence:

```text
artifacts/unity-client-playtest/<timestamp>/report.json
```

Raw reports are generated artifacts and must not be committed. The committed sanitized example is `docs/unity/examples/client-playtest-report.example.json`.

## Validation

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
```

Optional player build modes:

```bash
bash scripts/unity/run-unity-dev-build-smoke.sh --standalone-dev
bash scripts/unity/run-unity-dev-build-smoke.sh --webgl-dev
```

These write logs and any build output under ignored `artifacts/` paths.

## Limitations

Visuals are readable placeholders. Production art/audio, advanced animation timelines, production build pipelines, matchmaking/accounts/shop, and production hosting remain future work.

## Next Stage

The next stage should focus on art/audio asset integration and advanced presentation playback while preserving backend authority and hidden-information boundaries.
