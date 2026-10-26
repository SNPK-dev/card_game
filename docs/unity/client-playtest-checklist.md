# Unity Client Playtest Checklist

## Setup

- Start backend: `dotnet run --project src/Game.Api --configuration Release`.
- Open Unity project: `clients/unity/CardGame.Unity`.
- Open scene: `Assets/Scenes/CardGameLocalMatch.unity`.
- Confirm `CardGameClientRoot` exists.
- Confirm backend URL is `http://localhost:5000` or the configured local URL.

## Match Flow

- Boot client and verify capability status.
- Seed demo data.
- Start local match.
- Confirm PlayerOne state renders.
- Switch to PlayerTwo and confirm separate projection.
- Switch to Spectator and confirm command buttons are disabled.
- Switch to DebugBoth and confirm development-only warning.

## Commands And Interaction

- Submit `END_TURN` from a server-provided legal action.
- Submit a playable card if server legal actions provide one.
- Confirm own card faces show intentional frame/title/type/cost/stat/rules slots.
- Confirm opponent hand renders card backs/counts only.
- Confirm board lanes, hero panels, resources, damage badges, keywords, and active turn indication are readable.
- If target candidates are available, enter target mode and select only highlighted refs.
- Click a non-highlighted target and confirm no command is submitted.
- Cancel target mode.
- If a visible choice appears, select an enabled option and resolve it.
- Confirm options without ids are disabled.
- Confirm the target overlay, choice modal, reconnect control, replay control, terminal banner, and fault banner are visually clear.

## Recovery And Evidence

- Use reconnect and confirm state hash/event cursor updates.
- Run replay verify and confirm success status.
- Confirm event/presentation queue advances with visible card, board, turn, choice, reconnect, replay, terminal, and fault animations.
- Confirm audio cues are requested for button, command, card, combat, turn, choice, reconnect, replay, and fault events.
- Confirm mute and master/category volume controls affect audio requests.
- Export playtest evidence and confirm the raw report is under `artifacts/unity-client-playtest/`.
- Confirm the evidence includes playtest id, Unity version, backend URL, build target/mode, scenario, seed mode, match ids, player modes exercised, commands submitted, command types seen, targets, choices, reconnects, replay checks, theme version, audio profile, audio enabled, presentation intents played, animation counts, skipped animations, audio cues, hidden-info/spectator/terminal-fault check results, UI errors, manual tester notes placeholder, limitations, and verdict.
- Confirm no token or local secret appears in exported evidence.
- Confirm terminal/fault state disables commands.

## Hidden-Info Visual Check

- PlayerOne must not see PlayerTwo hidden hand/deck identities.
- PlayerTwo must not see PlayerOne hidden hand/deck identities.
- Spectator must not see hidden hand/deck identities.
- DebugBoth must not merge hidden identities across projection switches.

## WebGL/CORS

- Run `bash scripts/unity/run-webgl-cors-smoke.sh`.
- Confirm allowed origins pass and disallowed origins fail.

## Known Limitations

- Placeholder visuals only.
- No production art/audio.
- No matchmaking/accounts/shop.
- Full player build output is environment-dependent.
- Playtest evidence is a local manual aid and does not replace full live smoke.
- RC validation summaries are local artifacts and must not be committed raw.
- Gameplay rules remain backend-authoritative.
- Release-candidate verdict is `ready-with-known-limitations` until final commissioned media and live services exist.
