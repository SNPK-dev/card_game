# Unity Presentation

Presentation endpoints transform public events and asset manifests into deterministic playback hints. They do not affect gameplay state hash or replay.

## Flow

1. Poll or receive `PublicMatchEventRecord` values.
2. Load relevant `AssetManifest` values.
3. `POST /presentation/timelines`.
4. `POST /presentation/playback-plans`.
5. `POST /presentation/playback-instructions` with current client playback state.

The first event-to-animation mapping is `event-animation-map.md`. Presentation endpoints may provide cue timing/assets, but state refresh still follows the match state/event contract.

## Client Policy

- Unknown playback instructions are skipped after logging.
- Missing media follows `fallbackPolicy`: use fallback refs or silence.
- `PlayAsset` starts a cue, `Silence` reserves timing without media, and `Skip` means the runtime intentionally skipped playback.
- Gameplay state refresh is still driven by match state/events, not by presentation results.

## Usable UI Baseline

The local gameplay UI routes accepted command, poll, and reconnect events into presentation intents. The `PresentationArea` displays the current intent, playback status, and fallback diagnostics. The event feed keeps recent intent/event lines. Terminal and fault events block normal queue playback and remain visible until state recovery or match end. This baseline uses text-only built-in Unity UI and does not load production media.

Repeatable playtest pass adds deterministic placeholder timing to intents. Card-zone events show card movement text, board/combat events show combat/damage text, turn events show turn-changed text, choice events show choice opened/resolved text, reconnect/replay status displays immediately, and terminal/fault intents block normal queue playback.

## Polished Generated Presentation

The polished gameplay stage replaces text-only placeholder playback with deterministic animation bindings such as card play/draw/move, damage/heal/death, turn banners, choice emphasis, reconnect, replay, and fault banners. These are still generated/in-repo placeholders rather than final VFX, but they are routed as explicit presentation intents and animator bindings. Playback reports record animations played and skipped for playtest evidence.
