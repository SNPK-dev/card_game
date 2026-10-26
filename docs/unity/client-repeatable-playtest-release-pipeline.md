# Unity Repeatable Playtest And Release Pipeline

## Status

This stage makes repeated local playtests and release-candidate validation deterministic enough for development decisions. It remains placeholder-only and uses no production art/audio, external assets, matchmaking, accounts, shop, local gameplay rules, backend internals, or generator work.

## Idempotent Seed Strategy

Unity debug seeding uses public API endpoints only. Default seed mode is `Ensure`:

- read deterministic ruleset profile, catalog snapshot, and decks by public id;
- verify their expected Unity debug shape;
- create missing resources;
- accept compatible existing resources as `already-compatible`;
- fail incompatible resources clearly.

Seed modes:

- `Ensure`: default for live smoke and local scene seeding.
- `ResetIfSupported`: currently ensure-like because no public reset endpoint exists.
- `SkipIfExists`: skip compatible existing resources.
- `FailOnConflict`: fail if a deterministic resource already exists.

No unrelated user data is deleted or mutated.

## Collision-Safe Match IDs

Default full smoke match ids use:

```text
unity-live-smoke-<timestamp>-<short-random>
```

Manual local scene match ids use the same timestamp/random principle with a `unity-board-` prefix.

## Full Live Smoke Repeatability

The guarded PlayMode suite includes a repeatability test. When `CARDGAME_UNITY_LIVE_BACKEND_TESTS=1`, it runs two public API match flows with `SeedMode.Ensure`, verifies the second run accepts already-compatible seed data, uses a different match id, reconnects, and verifies replay.

Repeatability reports are raw artifacts under:

```text
artifacts/unity-client-live-smoke-repeatability/<timestamp>/report.json
```

Committed sanitized example: `docs/unity/examples/unity-live-smoke-repeatability-report.example.json`.

## RC Validation

Run:

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
bash scripts/unity/run-unity-client-rc-validation.sh
```

The script runs editor env checks, OpenAPI validation, DTO drift, backend smokes, standalone/WebGL build smokes, and enabled Unity PlayMode live smoke. It writes:

```text
artifacts/unity-rc-validation/<timestamp>/summary.json
```

Raw logs and summaries are ignored artifacts. Committed sanitized example: `docs/unity/examples/unity-client-rc-validation-summary.example.json`.

## Build Summaries

`scripts/unity/run-unity-dev-build-smoke.sh` writes per-attempt JSON summaries under:

```text
artifacts/unity-build-summaries/
```

Build output remains under ignored `artifacts/unity-dev-builds/`. If a target module is unavailable, the summary records a skip reason. If a build is reported passed, output must exist.

## Playtest Evidence

Manual evidence export includes playtest id, tester/scenario, backend URL, build mode, match ids, mode switches, commands, target selections, choices, reconnects, replay checks, presentation intents, errors, limitations, and verdict. Reports contain no raw tokens.

## Placeholder UI And Presentation

The UI uses larger built-in labels, face-up/card-back markers, target banners, disabled reasons, status/replay/connection lines, and diagnostics. Presentation intents have deterministic durations and placeholder animation semantics such as card movement, combat/damage, turn changed, choice opened/resolved, and terminal/fault banners.

## Limitations

Visuals remain placeholder-only. Production art/audio, real media playback polish, matchmaking/accounts/shop, production hosting, and final release packaging remain future stages.

## Next Stage

The next stage should focus on production asset/media integration and broader QA playtest operations without changing backend authority boundaries.
