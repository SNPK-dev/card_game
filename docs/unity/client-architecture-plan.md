# Unity Client Architecture Plan

The Unity client is a presentation and input layer over the public HTTP contract. It does not execute gameplay rules locally.

## Proposed Folder Layout

```text
Assets/CardGame/
  Runtime/
    Api/
    Config/
    Contracts/
    Match/
    State/
    Commands/
    Events/
    Presentation/
    Assets/
    Errors/
    Debug/
    UI/
      Shell/
      Cards/
      Board/
      Status/
      Interaction/
      Presentation/
  Editor/
  Tests/
    EditMode/
    PlayMode/
```

Proposed asmdefs: `CardGame.Contracts`, `CardGame.Runtime`, `CardGame.Editor`, `CardGame.Tests.EditMode`, and `CardGame.Tests.PlayMode`. The foundation pass creates only `Assets/CardGame/Runtime`, `Assets/CardGame/Editor`, and `Assets/CardGame/Tests`; asmdefs are deferred until the DTO import path is finalized in Stage 1.

Stage 1 implementation note: the first vertical slice keeps asmdefs deferred to avoid package-reference churn while the DTO mirror and Unity test runner are stabilized. Runtime code is under `Assets/CardGame/Runtime/**`, EditMode tests are under `Assets/CardGame/Editor/Tests/**`, and the debug UI is created at runtime instead of mutating `SampleScene.unity`.

Playable board slice note: the debug UI is no longer globally auto-created. Developers explicitly create it with `Card Game > Create Debug Client Root`. UI code lives under `Assets/CardGame/Runtime/UI/**`, and the debug bootstrapper now hosts the board screen instead of constructing a text dashboard directly.

Production gameplay shell note: UI view-model code is split by concern under `Runtime/UI/Shell`, `Runtime/UI/Cards`, `Runtime/UI/Board`, `Runtime/UI/Status`, `Runtime/UI/Interaction`, and `Runtime/UI/Presentation`. Factories are pure C# where possible and consume only public Unity DTO projections.

Usable gameplay UI note: binders under the same folders adapt those view models to built-in Unity UI panels. `DebugMatchBootstrapper` remains the local development orchestrator and now records manual playtest evidence plus text-only presentation playback feed entries.

Polished presentation note: `Runtime/UI/Theme` defines the generated in-repo theme system, `Runtime/Audio` defines model-level cue routing and mute/volume settings, and `Runtime/Presentation` includes deterministic animation bindings. These layers remain presentation-only and do not mutate authoritative match state.

## Component Map

### BackendApiClient

Responsibility: typed HTTP wrapper, JSON settings, session headers, cancellation, read retry policy, and response/error decoding.

Endpoints used: all public Unity endpoints through focused services.

DTOs used: request/response DTOs from `Game.UnityClient.Contracts`.

Lifecycle: initialized at boot from `UnityConfig`; shared by services.

Failure behavior: map transport and HTTP errors through `ErrorMapper`; retry reads only when safe.

Tests to add later: URL composition, header injection, JSON camelCase handling, unknown-field tolerance, retry boundaries.

Stage 1 status: implemented with `UnityWebRequest`, Newtonsoft JSON, session-token header support, cancellation/timeouts, diagnostics, and URL composition tests.

Must not do: implement gameplay rules, infer hidden state, or blindly retry commands.

### CapabilityChecker

Responsibility: boot compatibility gate.

Endpoints used: `GET /health`, `GET /engine/capabilities`.

DTOs used: health response and engine capability registry.

Lifecycle: runs before session or match creation.

Failure behavior: block boot on unsupported contract, command, event, state, asset, or playback schema.

Tests to add later: supported backend, unsupported command, unsupported schema, unreachable backend.

Stage 1 status: implemented for `/health` and `/engine/capabilities`; deeper schema compatibility gates remain future hardening.

Playable board status: required schema versions, command types, event types, and minimum Unity client contract version are validated before match creation.

Must not do: silently downgrade required gameplay capabilities.

### SessionService

Responsibility: create and store player/spectator sessions for local debug and future account flows.

Endpoints used: `POST /sessions`.

DTOs used: create session request/result.

Lifecycle: creates sessions during debug bootstrap or authenticated flow.

Failure behavior: recreate sessions after expired/forbidden boundaries when user flow allows.

Tests to add later: player session, spectator session, expired session recovery path.

Stage 1 status: implemented for debug player sessions.

Must not do: bypass session authorization or expose tokens in logs.

### MatchService

Responsibility: start matches, read current state, and verify replay.

Endpoints used: `POST /matches`, `GET /matches/{matchId}/state`, `POST /matches/{matchId}/replay/verify`.

DTOs used: match start request/result, `MatchStateView`, replay verification view.

Lifecycle: owns match id and delegates latest state to `MatchStateStore`.

Failure behavior: leave match setup on not found/forbidden; surface replay mismatch as blocking integrity failure.

Tests to add later: start match, state fetch, forbidden viewer, replay verified, replay mismatch.

Stage 1 status: implemented for start, state fetch, and replay verification.

Must not do: use event log as replay source.

### MatchStateStore

Responsibility: hold the latest server-projected state, state hash, command count, and next event sequence.

Endpoints used: none directly.

DTOs used: `MatchStateView`.

Lifecycle: updated after state fetch, command response, event refresh, or reconnect.

Failure behavior: replace local state on reconnect/hash mismatch; never merge hidden data across views.

Tests to add later: state replacement, event sequence update, player/spectator separation.

Stage 1 status: implemented with replacement-on-reconnect and event dedupe tests.

Must not do: mutate gameplay state locally or cache hidden opponent identities.

### LegalActionPresenter

Responsibility: expose server legal actions as interactable debug/production affordances.

Endpoints used: `GET /matches/{matchId}/players/{playerId}/legal-actions`.

DTOs used: legal action result and command templates.

Lifecycle: refreshes after state changes and active-player changes.

Failure behavior: disable action UI on forbidden/stale/error and request state refresh.

Tests to add later: visible command mapping, disabled unknown command, diagnostics display.

Must not do: infer legality when an action is absent.

Playable board status: legal actions are grouped by type, referenced cards/entities are highlighted, target-like actions without server target refs are disabled, and visible choices can be resolved through server option refs.

Stable local match status: `TargetSelectionController`, `TargetSelectionState`, and `TargetHighlightView` support target mode only when candidate refs are present in server-provided legal action templates. Spectator mode disables command submission.

### CommandBuilder

Responsibility: convert a selected legal action template into a command submission request.

Endpoints used: none directly.

DTOs used: legal action templates and command submission request.

Lifecycle: called by UI/debug controls immediately before queue submission.

Failure behavior: reject missing template refs or unsupported command types.

Tests to add later: `END_TURN`, simple `PLAY_CARD`, stale `expectedPreStateHash`, missing refs.

Stage 1 status: implemented to require server-provided legal action templates and expected pre-state hash.

Must not do: synthesize commands from local board rules.

### CommandQueue

Responsibility: serialize command submissions and block duplicate user actions until state advances.

Endpoints used: `POST /matches/{matchId}/commands`.

DTOs used: command submission request/result and public events from command responses.

Lifecycle: active during match scene.

Failure behavior: on stale hash, call reconnect/state refresh and require rebuilding command from current legal actions.

Tests to add later: single in-flight command, stale hash handling, network timeout reconnect path.

Must not do: blindly retry non-idempotent commands.

### EventPoller

Responsibility: poll viewer-filtered events by exclusive `afterEventSeq` cursor and dedupe by `eventSeq`.

Endpoints used: `GET /matches/{matchId}/events?afterEventSeq=N`.

DTOs used: event poll result and `PublicMatchEventRecord`.

Lifecycle: runs while match scene is active; slows while backgrounded.

Failure behavior: call reconnect after transport failures or sequence suspicion.

Tests to add later: polling interval, duplicate suppression, unknown event handling, terminal event handling.

Stage 1 status: implemented with event cursor polling and state refresh by the debug harness.

Must not do: treat events as replay source of truth.

### ReconnectService

Responsibility: recover state and missed events after app resume, network failures, stale hashes, or local mismatch.

Endpoints used: `POST /matches/{matchId}/reconnect?afterEventSeq=N`.

DTOs used: reconnect result, `MatchStateView`, public events.

Lifecycle: invoked by poller, command queue, and app lifecycle hooks.

Failure behavior: replace local state from server; recreate sessions on expired/forbidden response.

Tests to add later: missed event catch-up, hash mismatch replacement, expired session path.

Stage 1 status: implemented to call `/reconnect` and replace local state.

Must not do: patch local state from partial hidden data.

### ReplayVerifier

Responsibility: call replay verification at terminal/debug checkpoints and surface integrity failures.

Endpoints used: `POST /matches/{matchId}/replay/verify`.

DTOs used: replay verification view.

Lifecycle: invoked at terminal state or debug action.

Failure behavior: block trust in local animation/debug state when verification fails.

Tests to add later: verified replay, mismatch, unauthorized session.

Must not do: reconstruct replay from event logs.

### AssetManifestImporter

Responsibility: load asset manifests, resolve opaque asset refs, and apply fallbacks/placeholders.

Endpoints used: `GET /assets/manifests/current`, `GET /assets/manifests/{assetManifestId}`, compatibility and presentation-hash endpoints.

DTOs used: `AssetManifest`, visual/audio manifests, fallback policy, compatibility result.

Lifecycle: boot and match asset preload.

Failure behavior: use placeholders or silence according to fallback policy.

Tests to add later: manifest load, missing art fallback, silence allowed, compatibility failure.

Must not do: let assets affect gameplay state hash or legality.

### PresentationPlaybackController

Responsibility: turn public events and manifests into baseline presentation playback.

Endpoints used: `POST /presentation/timelines`, `POST /presentation/playback-plans`, `POST /presentation/playback-instructions`.

DTOs used: presentation timeline, playback plan, playback instructions, public events, asset manifests.

Lifecycle: optional baseline after event polling is stable.

Failure behavior: skip unknown instructions after logging; refresh state remains authoritative.

Tests to add later: timeline request, plan request, unknown instruction skip, missing media fallback.

Must not do: drive gameplay state changes from playback instructions.

Playable board status: full playback endpoints remain deferred; `EventVisualDispatcher` provides a local baseline message layer over public events and keeps state refresh authoritative.

Stable local match status: `EventVisualQueue` and `EventVisualTiming` add sequencing, dedupe, terminal interruption, and fallback timing for local visuals without mutating `MatchStateView`.

Full live smoke status: `LocalMatchFlowDriver` orchestrates a full public API backend flow for PlayMode smoke without UI clicks, backend internals, local legality inference, or raw token reporting.

Repeatable playtest status: `LocalMatchFlowDriver` uses collision-safe smoke match ids and structured seed reporting. `DebugDemoSeeder` verifies deterministic ruleset/catalog/deck resources through public reads before creating missing data, and `SeedMode.Ensure` accepts compatible existing resources.

### ErrorMapper

Responsibility: map HTTP status, diagnostics, and Unity error shapes into UI/debug actions.

Endpoints used: none directly.

DTOs used: diagnostic and Unity error shapes.

Lifecycle: shared by API client and services.

Failure behavior: classify retryable read errors, stale hash recovery, forbidden sessions, replay mismatch, and server fault.

Tests to add later: `COMMAND_PRE_STATE_HASH_MISMATCH`, `MATCH_SESSION_FORBIDDEN`, `RATE_LIMITED`, `ENGINE_FAULTED`, unknown error.

Must not do: hide blocking integrity or compatibility failures.

### UnityConfig

Responsibility: hold backend base URL, polling interval, timeout, WebGL origin assumptions, and debug flags.

Endpoints used: none directly.

DTOs used: none.

Lifecycle: loaded at boot from ScriptableObject and environment/platform overrides.

Failure behavior: invalid URL blocks boot with a local configuration error.

Tests to add later: local default URL, environment override, WebGL config, invalid URL.

Must not do: store secrets or production credentials.

### DebugMatchBootstrapper

Responsibility: Stage 1/2 debug-only boot flow for local development.

Endpoints used: health, capabilities, storage/demo endpoints when needed, sessions, matches, state, legal actions.

DTOs used: boot, catalog/profile/deck, session, match, state, legal action DTOs.

Lifecycle: development scene component or editor command.

Failure behavior: report exact failing step and stop before pretending the match is playable.

Tests to add later: happy-path local bootstrap, missing demo data, unavailable backend.

Must not do: become production matchmaking, production UI, or hidden backend dependency.

Stage 1 status: implemented as a runtime-created debug UI over public endpoints, with optional public storage seeding and player-view switching for local development.
