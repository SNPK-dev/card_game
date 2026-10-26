# Unity Client Audio Direction

## Status

The polished gameplay stage adds model-level audio routing and generated cue references. It does not import copyrighted or external audio.

## Cue Categories

- UI
- Card
- Combat
- Turn
- Choice
- Connection
- Replay
- Fault

## Cue Routing

`AudioEventRouter` maps UI/game events to deterministic cue ids. `AudioCueRegistry` resolves cue ids to generated asset refs. `AudioPlaybackController` emits playback requests after mute and volume settings are applied.

## Controls

`AudioVolumeSettings` supports:

- mute toggle;
- master volume;
- category volume;
- safe clamping from 0 to 1;
- missing-cue fallback without exceptions.

## Generated Assets

Generated cue refs use `generated://` ids and are license-safe. If WAV generation is added later, use `scripts/unity/generate-placeholder-audio.sh` and keep generated files small, deterministic, and repo-owned.

## Test Policy

Tests and build smoke may disable playback. Audio routing must not affect gameplay state hash, legal actions, replay, or hidden information.
