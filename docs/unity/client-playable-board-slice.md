# Unity Client Playable Board Slice

## Implemented

This slice upgrades the debug dashboard into a minimal playable board/hand client:

- board/hand screen built with Unity built-in UI;
- player hand placeholders for visible own cards;
- opponent hand/deck count placeholders only;
- player and opponent board rows with public entity stats;
- hero/resource summaries;
- grouped legal actions and highlighted referenced cards/entities;
- visible choice option rendering and `RESOLVE_CHOICE` submission from server refs;
- event visual messages for core public events;
- reconnect and replay status surfaced in the board UI;
- stricter capability boot checks;
- deterministic DTO mirror sync and drift scripts.

## Running The Backend

From the repository root, run the local backend flow documented in `docs/unity/local-smoke.md`. Before opening Unity, these checks are useful:

```bash
bash scripts/unity/run-local-smoke.sh
bash scripts/unity/run-bot-match-smoke.sh
```

## Running Unity

Open:

```text
clients/unity/CardGame.Unity
```

The debug client is opt-in. It no longer auto-creates in every scene.

To add it to the active scene:

```text
Unity menu: Card Game > Create Debug Client Root
```

Then enter Play Mode.

## Demo Data

Use the `Seed` button to publish deterministic local demo profile/catalog/decks through public endpoints:

- `POST /rulesets/profiles`
- `POST /catalog/snapshots`
- `POST /decks`

Unity does not read backend files directly.

## Board And Hand UI

- Own hand renders visible `CardId`, card type, and visibility from `MatchStateView`.
- Opponent hand and hidden zones render counts/card backs only.
- Boards render public `PublicEntityView` data: entity id, type, attack, health after damage, raw damage, controller, and keywords.
- Hero, weapon, location, mana, armor, fatigue, deck count, and hand count render from the server projection.

## Legal Actions

Legal actions are fetched from:

```text
GET /matches/{matchId}/players/{playerId}/legal-actions
```

The UI groups actions by command type and highlights cards/entities referenced by server templates. Complete action templates submit directly to:

```text
POST /matches/{matchId}/commands
```

The client includes `expectedPreStateHash`.

If a target-like action lacks server-provided target refs, the action is disabled with a diagnostic. Unity does not infer targets from board rules.

## Choices

Visible pending choices render option buttons. The client submits `RESOLVE_CHOICE` using server-provided `choiceId` and `selectedOptionId`. Private choices are shown only when present in the current viewer projection.

## Event Visual Feedback

`EventVisualDispatcher` maps core public events to short messages and refresh hints. Unknown future events use a generic fallback and refresh state. Terminal and fault events are marked blocking.

## Reconnect And Replay

Reconnect is available as a button and is triggered after stale hash, polling transport error, and app focus/resume. Reconnect replaces local state from the server projection and appends missed events once.

Replay verification displays verified/mismatch status, diagnostics, and final state hash. Mismatch is treated as blocking evidence failure.

## Hidden Information Safety

The UI renders only the current `MatchStateView` projection. It does not cache or merge hidden identities across viewer switches and does not infer opponent cards from deck lists or command payloads.

## PlayMode / Live Backend Smoke

Unity EditMode tests cover client logic. Live-backend Unity smoke is documented as a manual guarded flow for this pass because the project does not yet have PlayMode test asmdefs or a CI-safe Unity test runner configuration for live HTTP tests. The repository backend smoke scripts still exercise the public match flow.

## Limitations

- UI is still a development board, not production polish.
- No production art, audio, animation timelines, matchmaking, accounts, or shop.
- Target selection is limited to refs already present in legal action templates.
- Spectator UI is not yet a dedicated mode.
