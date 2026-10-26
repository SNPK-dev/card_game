# Unity Polished Gameplay Presentation

## Status

Verdict target for this stage is `unity-client-polished-gameplay-presentation-ready-with-limitations` because visuals and audio are generated/in-repo placeholders, not final commissioned production media.

## What Changed

- Added a coherent theme model with palette, typography, spacing, sprite refs, and audio cue ids.
- Added card face/back/frame/cost/stats/rules/keyword/glow/disabled overlay view models.
- Added board lane/entity/hero/resource/damage/keyword/target-ring presentation models.
- Added command button, command group, target overlay, choice modal, reconnect, replay, and toast presentation models.
- Added deterministic event-to-animation bindings and named presentation animators.
- Added model-level audio routing, cue registry, playback requests, mute, and volume controls.
- Extended playtest evidence with theme/audio/presentation/animation/UI-error fields.
- Updated build smoke to validate polished hierarchy and prefab assets.

## Authority Boundary

Unity still consumes only public backend state, legal actions, choices, events, reconnect, and replay endpoints. It does not implement gameplay rules locally, infer hidden identities, use backend internals, or use presentation/audio state as gameplay input.

## Local Playtest

```bash
dotnet run --project src/Game.Api --configuration Release
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
bash scripts/unity/run-unity-client-rc-validation.sh
```

## Remaining Limitations

- Generated/in-repo placeholder visual and audio refs remain.
- No matchmaking, accounts, shop, production hosting, or final art pipeline.
- Standalone/WebGL player generation remains dependent on installed Unity modules.
- Interactive editor inspection is now tracked separately by `docs/unity/mcp-inspection.md`; MCP is a local editor gate and does not replace batchmode RC validation.
