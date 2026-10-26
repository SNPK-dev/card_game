# Unity Integration Docs

This package is the Unity developer entry point. Unity should start with `integration-guide.md`, then use the focused contract pages below while implementing client services and DTOs.

## Documents

- `integration-guide.md` - concise end-to-end boot, match, command, event, reconnect, replay, asset, and smoke flow.
- `api.md` - endpoint list, headers, and request/response ownership.
- `state.md` - player, opponent, spectator, reconnect, and hidden-information projection rules.
- `commands.md` - legal actions and command payload contract.
- `events.md` - typed public event contract and event handling matrix.
- `event-animation-map.md` - initial event-to-animation mapping for the first Unity animation system.
- `effect-coverage.md` - current mechanic/effect readiness matrix for Unity consumption.
- `assets.md` - asset manifest contract and fallback policy.
- `presentation.md` - timeline, playback plan, and playback instruction contract.
- `errors.md` - Unity-facing error/status model.
- `versioning.md` - capability boot check and compatibility policy.
- `realtime.md` - realtime decision for v1.
- `webgl.md` - configurable CORS policy and WebGL smoke.
- `local-smoke.md` - local smoke and two-bot smoke instructions.
- `client-local-match.md` - stable local match scene, modes, target/choice, reconnect, replay, and Unity smoke flow.
- `client-full-live-smoke.md` - guarded Unity PlayMode full live backend match smoke and report flow.
- `client-production-gameplay-shell.md` - production-facing placeholder shell, view-model, interaction, presentation, and QA structure.
- `client-usable-gameplay-ui.md` - usable built-in Unity gameplay UI, presentation feed, reconnect/replay diagnostics, and playtest evidence export.
- `client-repeatable-playtest-release-pipeline.md` - idempotent seed flow, repeatable full live smoke, RC validation, build summaries, and playtest evidence loop.
- `client-polished-gameplay-presentation.md` - polished generated/in-repo gameplay presentation, animation, audio routing, validation, and limitations.
- `client-release-candidate.md` - scoped Unity release-candidate status, validation, build, go/no-go, and limitations.
- `client-release-candidate-implementation-note.md` - implementation note for the release-candidate hardening pass.
- `client-production-readiness-audit.md` - production-readiness audit by category with evidence and risks.
- `client-media-inventory.md` and `client-media-license-policy.md` - generated/repo-owned media inventory and license boundaries.
- `client-performance-baseline.md` and `client-readability-accessibility-baseline.md` - lightweight RC quality/readability baseline.
- `client-authority-and-hidden-info-final-gate.md` - final Unity authority and hidden-information gate.
- `mcp-inspection.md` - manual unityMCP startup, endpoint diagnostic, editor inspection checklist, failure modes, and MCP-vs-batchmode responsibilities.
- `mcp-inspection-latest.md` - latest sanitized interactive Unity Editor inspection evidence.
- `client-visual-direction.md` - visual theme, generated asset policy, card/board/interaction treatment, and limitations.
- `client-audio-direction.md` - audio cue routing, mute/volume controls, generated cue policy, and test policy.
- `client-playtest-checklist.md` - manual local playtest checklist for the current shell.
- `dev-build-smoke.md` - Unity batchmode development build-smoke validation notes.
- `architecture.md` - recommended Unity client components.
- `generated-contracts.md` - Unity C# DTO package location and verification.
- `examples/` - JSON examples that are validated by integration tests.

## Unity Must Not

- Implement engine rules locally.
- Depend on hidden zones, deck order, private choices, RNG/debug traces, or raw domain DTOs.
- Treat presentation assets as gameplay input.
- Submit commands that are not returned by the legal-action endpoint.
- Use event logs as replay source of truth. Replay is verified from the canonical command log.

The machine-readable contract is `../api/openapi.yaml`. The Unity C# DTO package is `../../src/Game.UnityClient.Contracts/`.
