# Unity Client Vertical Slice

## Implemented

The first playable Unity debug slice adds:

- `UnityBackendConfig` with default local backend URL `http://localhost:5000`.
- `BackendApiClient` using `UnityWebRequest`, JSON GET/POST, `X-Session-Token`, cancellation, timeouts, and error decoding.
- Capability, session, match, legal action, command, event polling, reconnect, replay verification, state store, and error mapper services.
- An opt-in built-in Unity UI for boot, match metadata, player/opponent state, board, legal actions, event log, and errors.
- Optional debug seeding through documented public storage endpoints for a minimal profile, catalog, and two decks.
- EditMode tests for config, URL composition, command building, state replacement, event dedupe, and error mapping.

## Run Locally

1. Start the backend from the repository root using the documented local backend flow.
2. Optional pre-check:

```bash
bash scripts/unity/run-local-smoke.sh
bash scripts/unity/run-bot-match-smoke.sh
```

3. Open Unity project:

```text
clients/unity/CardGame.Unity
```

4. Open a development scene.
5. Add the debug client root with `Card Game > Create Debug Client Root`.
6. Enter Play Mode and use the board/debug buttons:

- `Boot`: calls `/health` and `/engine/capabilities`.
- `Seed`: posts minimal debug profile/catalog/decks through public storage endpoints.
- `Start`: creates two sessions, starts a debug match, fetches state, and fetches legal actions.
- `Switch`: switches between the two player projections for local development.
- `State`: refreshes the authoritative match state.
- `Legal`: refreshes server legal actions.
- `End Turn` and `Play Card`: submit the first matching server-provided legal action template with `expectedPreStateHash`.
- `Reconnect`: calls `/matches/{matchId}/reconnect?afterEventSeq=N` and replaces local state.
- `Replay`: calls replay verification and displays the result.
- `Poll`: starts or stops event polling.

## Hidden Information

The UI displays own hand identities only for the active viewer projection. Opponent hand and deck are rendered as counts. The debug client does not infer identities from deck lists, command payloads, or cached state.

## Demo Data

Unity can seed deterministic local debug data through public endpoints:

- `POST /rulesets/profiles`
- `POST /catalog/snapshots`
- `POST /decks`

If seeding fails, run the backend smoke seed flow and retry `Start`. Unity does not read backend files directly.

## Placeholder Scope

The UI is intentionally a debug harness. It uses built-in Unity UI, default font, text panels, and buttons only. It does not include production board layout, art, animation, audio, account login, matchmaking, targeting UI, or build pipeline work.

## Later Stage Note

The playable board slice replaced the original text-only dashboard with a minimal board/hand layout while keeping backend state authoritative.

The stable local match pass adds a repeatable `CardGameLocalMatch.unity` scene, explicit player/spectator/debug modes, server-provided target selection, queued event visuals, reconnect command disabling, and guarded Unity smoke assets.
