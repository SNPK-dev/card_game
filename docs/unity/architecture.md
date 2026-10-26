# Unity Client Architecture

Recommended components:

- `BackendApiClient`: typed HTTP, headers, JSON options, retries for retryable errors.
- `GeneratedContracts`: DTOs from `src/Game.UnityClient.Contracts`, imported as source or DLL.
- `CapabilityChecker`: calls `/engine/capabilities` and blocks incompatible clients.
- `SessionService`: creates and refreshes player/spectator sessions.
- `MatchService`: starts matches and owns match endpoint orchestration.
- `MatchStateStore`: stores latest `MatchStateView`, state hash, command count, and next event seq.
- `LegalActionPresenter`: renders backend-provided legal actions without engine rules.
- `CommandBuilder`: maps selected legal actions to command submissions.
- `CommandQueue`: serializes command submissions and handles stale hash refresh.
- `EventPoller`: polls events and deduplicates by `eventSeq`.
- `ReconnectService`: rebuilds state and missed event stream after drops.
- `ReplayVerifier`: calls replay verification after matches or smoke runs.
- `AssetManifestImporter`: resolves asset refs and placeholder fallbacks.
- `PresentationPlaybackController`: maps public events to timelines, plans, and instructions.
- `ErrorMapper`: maps `UnityError`/diagnostics to UI behavior.
- `LocalSmokeRunner`: mirrors `scripts/unity/run-local-smoke.sh` for Unity-side validation.
- `BotMatchDebugRunner`: optional Unity-side harness that mirrors the public API bot smoke.

Do not implement legality, RNG, state mutation, hidden-info inference, or replay locally.

First Unity milestone: connect to local backend, create sessions, start match, render state, render legal actions, submit one command, poll events, reconnect, verify replay, load fallback-capable assets, and play baseline animations from `event-animation-map.md`.
