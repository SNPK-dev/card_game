# Unity Client Roadmap

This roadmap starts after the backend/API/docs readiness baseline. It is intentionally practical and checkbox-based; each stage should leave evidence before the next one expands scope.

## Stage 0 - Foundation

Goal: make the Unity workspace safe to develop in.

Checklist:
- [x] Sync `codex/unity_client` with backend Unity-readiness changes.
- [x] Configure Unity `.gitignore` rules.
- [x] Verify Unity project source, project settings, packages, and `.meta` files.
- [x] Verify Unity Editor reachability through `unityMCP`.
- [x] Create the Unity client OpenSpec change.
- [x] Review Unity docs, OpenAPI, DTO package, and smoke scripts.
- [x] Decide initial folder/package architecture.

Expected evidence: branch SHA, readiness commit reachability, `git status --ignored`, MCP project info/editor state/console checks, roadmap, architecture plan, and OpenSpec validation.

Definition of done: no Unity cache/generated folders are staged, the editor is reachable or an exact blocker is recorded, and foundation docs are committed.

Out of scope: gameplay UI, backend changes, production art, generator work, and full client systems.

## Stage 1 - Backend Connectivity Vertical Slice

Goal: boot the client against the local backend and display compatibility status.

Checklist:
- [x] Import or reference `Game.UnityClient.Contracts`.
- [x] Configure backend base URL.
- [x] Implement `BackendApiClient`.
- [x] Call `/health`.
- [x] Call `/engine/capabilities`.
- [x] Display boot status in Unity.
- [x] Handle compatibility errors.

Expected evidence: EditMode tests for URL/config/error mapping, PlayMode/manual screenshot or log, and successful local backend boot calls.

Definition of done: Unity can fail fast on incompatible backend capabilities and show a successful local boot state.

Out of scope: match UI, command submission, event animations, and asset playback.

## Stage 2 - Session And Match Bootstrap

Goal: start a local debug match using public APIs only.

Checklist:
- [x] Create two sessions.
- [x] Load/publish demo data if needed.
- [x] Start match.
- [x] Fetch player state.
- [x] Render minimal debug state.

Expected evidence: session tokens, match id, player state hash, and a minimal debug view using `MatchStateView`.

Definition of done: a fresh Unity run can create a local debug match and show state metadata without backend internals.

Out of scope: polished board layout and production login/account flow.

## Stage 3 - Legal Actions And Commands

Goal: drive one legal command from server-provided legal actions.

Checklist:
- [x] Fetch legal actions.
- [x] Render debug buttons.
- [x] Submit `END_TURN`.
- [x] Submit simple `PLAY_CARD` if available.
- [x] Handle stale state hash.
- [x] Reconnect after command.

Expected evidence: legal action payload, command request, command response, updated state hash, and stale-hash handling test.

Definition of done: Unity submits only legal-action-derived commands and recovers from stale state.

Out of scope: full card targeting UI and local legality inference.

## Stage 4 - Event Polling

Goal: consume public events with explicit cursors.

Checklist:
- [x] Poll `/events`.
- [x] Dedupe by `eventSeq`.
- [x] Map events to debug log.
- [x] Refresh state on events.
- [x] Implement reconnect fallback.

Expected evidence: monotonic event log, duplicate suppression test, reconnect after simulated transport failure.

Definition of done: Unity maintains last seen event sequence and treats state refresh/reconnect as authoritative.

Out of scope: realtime push and polished animation sequencing.

## Stage 5 - Minimal Board/Hand UI

Goal: display a playable debug match while respecting hidden information.

Checklist:
- [x] Render own hand.
- [x] Render opponent counters.
- [x] Render board.
- [x] Render hero/resources.
- [x] Hide hidden information.

Expected evidence: player/spectator screenshots, hidden-info assertions, and state projection tests.

Definition of done: visible state matches the server projection for player and spectator views.

Out of scope: final visual design and production UX polish.

Stage status: implemented as an opt-in development board/hand UI with placeholder panels, grouped legal actions, visible choice buttons, event visual messages, and hidden-info-safe opponent counts. Stable local match work adds the dedicated scene, spectator/debug modes, target selection, event queue, and reconnect command disabling.

## Stage 6 - Presentation Baseline

Goal: add placeholder rendering and baseline event-to-animation hooks.

Checklist:
- [ ] Load asset manifests.
- [ ] Apply placeholder art.
- [x] Implement event-to-animation baseline.
- [ ] Integrate playback plans where useful.

Expected evidence: manifest load logs, placeholder fallback behavior, and event animation mapping coverage.

Definition of done: missing assets do not block gameplay and presentation does not affect gameplay hashes.

Out of scope: production art, audio polish, and advanced timelines.

Stage status: initial event-to-visual message baseline is implemented. Asset manifest loading and playback-plan integration remain open.

## Stage 7 - Two-Player Local Flow

Goal: support local development with both player perspectives.

Checklist:
- [x] Player 1 and player 2 views.
- [x] Spectator/debug view.
- [x] Switch active player locally for development.
- [x] Verify hidden-info policy visually.

Expected evidence: side-by-side or switchable debug views and hidden-information validation notes.

Definition of done: local developers can inspect both perspectives without leaking hidden state in any single player view.

Out of scope: online matchmaking and authentication UX.

Stage status: `PlayerOne`, `PlayerTwo`, `Spectator`, and `DebugBoth` modes exist. Spectator disables commands, and DebugBoth switches projections instead of merging hidden data.

## Stage 8 - Build/WebGL Preparation

Goal: prove client builds can call configured endpoints.

Checklist:
- [x] Configure CORS-compatible endpoint.
- [x] Run WebGL/CORS smoke.
- [x] Add standalone/dev build smoke validation.
- [ ] Build WebGL dev build if supported.

Expected evidence: build logs, CORS smoke output, and endpoint configuration notes.

Definition of done: standalone and WebGL development builds are either passing or have exact environment blockers.

Out of scope: production hosting and store packaging.

Stage status: Unity includes editor build-smoke validation for the local match scene. Actual build output remains environment-dependent and is not committed.

## Stage 9 - Polish And Production Client Work

Goal: move from debug client to production client.

Checklist:
- [x] UI/UX polish with generated in-repo theme.
- [ ] Real commissioned assets.
- [x] Animation polish through deterministic presentation bindings.
- [x] Audio polish through generated cue routing.
- [x] Error screens/fault banners.
- [x] Reconnection UX presentation.
- [x] Build pipeline validation for polished hierarchy/assets.

Expected evidence: production UI review, asset integration reports, animation/audio QA, and build pipeline results.

Definition of done: the client is ready for broader playtest and production hardening.

Out of scope: new backend gameplay mechanics unless separately approved through OpenSpec.

Stage status: polished gameplay presentation ready-with-limitations. The client now has coherent generated/in-repo visual/audio presentation and deterministic animation/audio routing, while final commissioned art/audio remains future work.

## Stage 10 - Full Unity Live Match Smoke

Goal: prove the Unity client can drive a full local backend match flow from Unity-side code.

Checklist:
- [x] Full public API match-flow driver.
- [x] Guarded PlayMode full live smoke.
- [x] Structured smoke report.
- [x] Hidden-information assertions.
- [x] Batchmode script environment reporting.
- [x] Enabled Unity PlayMode batchmode run with sanitized evidence.

Expected evidence: report JSON, guarded PlayMode test result, backend smoke scripts, and Unity MCP compile/test checks.

Definition of done: enabled PlayMode smoke passes when backend and Unity batchmode are available, or implementation is accepted with environment-limited validation when `UNITY_EDITOR` is unavailable.

Stage status: promoted on 2026-07-08 by an enabled Unity PlayMode batchmode run. The smoke submitted `END_TURN`, advanced command count from 0 to 1, observed 2 events, reconnected successfully, verified replay, and passed hidden-information assertions.

Next stages: production UI shell, presentation playback integration, standalone/WebGL build validation, full targeting/choice UX expansion, and QA/playtest loop.

## Stage 11 - Production-Facing Gameplay Shell

Goal: turn the validated local playable client into a clean production-facing placeholder gameplay shell.

Checklist:
- [x] Runtime UI folders for shell, cards, board, status, interaction, and presentation.
- [x] Hidden-info-safe card view models.
- [x] Board/entity/resource/status view models.
- [x] Server-driven legal action and command guard models.
- [x] Backend-candidate-driven target view models.
- [x] Server-id-driven choice view models.
- [x] Presentation intent queue coverage for known public events.
- [x] Connection/reconnect/replay/capability status models.
- [x] Scene root placeholder hierarchy.
- [x] QA/playtest checklist.

Expected evidence: EditMode tests, full live PlayMode smoke, dev build validation, OpenSpec validation, and updated docs.

Definition of done: gameplay shell is ready / ready-with-limitations for production-facing iteration without final art/audio.

Stage status: production-facing gameplay shell ready with placeholder visuals and environment-dependent full player builds.

## Stage 12 - Usable Gameplay UI And Presentation Playback

Goal: make the local match scene readable and useful for repeated playtests without final art/audio.

Checklist:
- [x] Built-in Unity UI binders for shell, cards, board, status, interaction, presentation, event feed, and diagnostics.
- [x] Named local playtest layout regions under `CardGameClientRoot/Canvas`.
- [x] Gameplay screen state machine for boot, seed, start, active, target, choice, submit, reconnect, replay, terminal, fault, spectator, and DebugBoth states.
- [x] Hidden-info-safe hand/card binders.
- [x] Deterministic board entity surfaces and backend-candidate target highlighting.
- [x] Legal action buttons bound to server templates and expected pre-state hashes.
- [x] Choice panel option enablement from server-provided ids.
- [x] Presentation playback runner and event feed.
- [x] Reconnect/replay diagnostics models.
- [x] Local playtest evidence recorder/exporter.
- [x] Dev build smoke validates Canvas gameplay regions and smoke scripts.

Expected evidence: EditMode tests, full live PlayMode smoke, dev build validation, OpenSpec validation, playtest report example, and updated docs.

Definition of done: usable gameplay UI and presentation playback are ready / ready-with-limitations for repeated local playtests using placeholder visuals only.

Stage status: usable gameplay UI + presentation playback ready with placeholder visuals and environment-dependent standalone/WebGL build modules.

## Stage 13 - Repeatable Playtest And Release-Candidate Pipeline

Goal: make repeated local playtests and release-candidate validation reliable.

Checklist:
- [x] Idempotent public-API Unity debug seed strategy.
- [x] Per-resource seed compatibility reporting.
- [x] Collision-safe smoke and manual match ids.
- [x] Guarded full-smoke repeatability proof.
- [x] Live smoke report diagnostics for seed, backend precheck, match id strategy, test/log paths, environment, reconnect, replay, and hidden-info checks.
- [x] Placeholder UI readability pass with built-in Unity UI only.
- [x] Deterministic placeholder presentation timing and status intents.
- [x] Expanded token-safe playtest evidence.
- [x] Standalone/WebGL build summary JSON reporting.
- [x] Unity client RC validation script.

Expected evidence: EditMode tests, full enabled PlayMode smoke, repeatability report, dev build summaries, RC validation summary, OpenSpec validation, and updated docs/examples.

Definition of done: repeatable playtest + release-candidate pipeline is ready / ready-with-limitations for local RC decisions without production media.

Stage status: repeatable playtest + release-candidate pipeline in progress for this change.

## Stage 14 - Polished Gameplay Presentation

Goal: replace placeholder appearance and placeholder presentation with a real playable generated/in-repo visual/audio gameplay experience.

Checklist:
- [x] Visual theme and generated asset refs.
- [x] Card/board/hero/interaction presentation models.
- [x] Target/choice/reconnect/replay/terminal visual treatment.
- [x] Deterministic presentation animation bindings.
- [x] Audio cue routing and mute/volume models.
- [x] Playtest evidence fields for theme/audio/animations/UI errors.
- [x] Build smoke validation for polished hierarchy and prefabs.

Stage status: ready-with-limitations.

## Stage 15 - MCP Inspection Gate

Goal: restore `unityMCP` as the required interactive Unity Editor inspection layer on top of mandatory batchmode validation.

Checklist:
- [x] Manual MCP startup command documented.
- [x] Endpoint health diagnostic script.
- [x] MCP tools/resources listed.
- [x] Active Unity instance, project path, Unity version, and editor readiness inspected.
- [x] `Assets/Scenes/CardGameLocalMatch.unity` loaded through MCP.
- [x] `CardGameClientRoot` and polished hierarchy inspected through MCP.
- [x] Missing scripts and runtime bootstrap equivalents recorded.
- [x] Console and short Play Mode sanity inspected through MCP.
- [x] MCP-vs-batchmode validation responsibility documented.

Expected evidence: `docs/unity/mcp-inspection-latest.md`, `docs/unity/examples/mcp-inspection-report.example.json`, endpoint script output, RC validation, full live PlayMode smoke, repeatability proof, and dev build smoke.

Definition of done: MCP inspection is ready only when the active Unity project is actually inspected through MCP and mandatory batchmode validation remains green.

Stage status: in progress for `unity-client-mcp-inspection-gate`.

## Stage 16 - Release Candidate

Goal: harden the polished Unity client into a scoped release candidate for local/editor, standalone, and WebGL gameplay validation.

Checklist:
- [x] Release-candidate OpenSpec change.
- [x] Timestamped build packaging summaries.
- [x] Media inventory and license gate.
- [x] Performance/readability quality gates.
- [x] Authority and hidden-info final gate.
- [x] Production readiness audit.
- [x] RC playtest evidence example.
- [x] RC validation aggregation.

Stage status: Unity client release candidate: ready with known limitations.
