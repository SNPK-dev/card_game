# Unity Client Performance Baseline

## RC Baseline

The RC baseline is lightweight and deterministic. It is not a full profiler budget.

## Layout Sanity Targets

- 1920x1080
- 1366x768
- 1280x720

Core panels are fixed under `CardGameClientRoot/Canvas`: `Header`, `OpponentPanel`, `OpponentHand`, `Board`, `PlayerPanel`, `PlayerHand`, `InteractionPanel`, `PresentationOverlay`, `ChoiceModal`, `TargetOverlay`, `EventFeed`, `DiagnosticsPanel`, and `AudioControls`.

## Runtime Bounds

- event feed bounded by the runtime event feed implementation;
- presentation queue bounded by playback runner/evidence skipped-animation tracking;
- no unbounded UI object growth is allowed during smoke/playtest;
- diagnostics panel must remain secondary and not dominate gameplay screen.

## Validation

```bash
bash scripts/unity/run-unity-client-quality-gates.sh
```

Manual playtest should also confirm common-resolution readability before broader playtest.
