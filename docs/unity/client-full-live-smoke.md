# Unity Full Live Match Smoke

## Backend Prerequisite

Start the local backend before running the enabled Unity smoke:

```bash
dotnet run --project src/Game.Api
```

The default backend URL is `http://localhost:5000`.

## Unity Editor Prerequisite

Set `UNITY_EDITOR` to the Unity `6000.3.2f1` executable when running batchmode scripts:

```bash
export UNITY_EDITOR="/path/to/Unity.exe"
```

On Windows PowerShell the executable is typically:

```text
C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe
```

When running these Bash scripts from WSL, use the mounted path form:

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
```

## Run Guarded PlayMode Smoke

```bash
bash scripts/unity/run-unity-playmode-smoke.sh
```

The script sets `CARDGAME_UNITY_LIVE_BACKEND_TESTS=1`, runs PlayMode tests only, writes Unity logs under `artifacts/unity-test-logs/`, and expects the backend to already be running.

For a local preflight that also checks `/health` and runs the backend local smoke first:

```bash
bash scripts/unity/run-unity-full-live-smoke-local.sh
```

## Environment Variables

- `UNITY_EDITOR`: required for batchmode scripts.
- `UNITY_PROJECT_PATH`: optional override, default `clients/unity/CardGame.Unity`.
- `UNITY_BACKEND_URL`: optional helper-script health URL, default `http://localhost:5000`.
- `UNITY_TEST_LOG_ROOT`: optional Unity log output root, default `artifacts/unity-test-logs`.
- `CARDGAME_UNITY_LIVE_BACKEND_TESTS`: test guard; the batchmode script sets it to `1`.

## Report Path

The PlayMode test writes:

```text
artifacts/unity-client-live-smoke/<timestamp>/report.json
```

Raw session tokens are not written. Token fields are booleans indicating presence.

The default seed mode is `Ensure`: compatible existing deterministic debug resources are accepted, missing resources are created, and incompatible resources fail clearly. The report records per-resource seed status such as `created`, `already-compatible`, `skipped`, `incompatible`, or `failed`. Default match ids are collision-safe: `unity-live-smoke-<timestamp>-<short-random>`.

## Actual Run Evidence

On 2026-07-08, the enabled Unity PlayMode batchmode smoke ran against `http://localhost:5000` with:

```bash
UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe" CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
```

Result summary:

- test XML: `artifacts/unity-test-logs/playmode-smoke-20260708T082218Z-results.xml`
- raw report: `artifacts/unity-client-live-smoke/2026-07-08T0822285710229Z0000/report.json`
- sanitized committed evidence: `docs/unity/examples/unity-live-smoke-report.actual-sanitized.json`
- final verdict: `unity-client-full-live-match-smoke-ready`
- command submitted: `END_TURN`
- command count: `0 -> 1`
- event count: `2`
- reconnect: succeeded
- replay verification: succeeded
- hidden-information assertions: passed

## Report Fields

The report includes started/completed timestamps, Unity version, backend URL, match id, player ids, spectator id, token presence, seed status, boot/capability status, state hashes, submitted command, command counts, event count, last event sequence, reconnect hashes/cursors/missed-event count, replay verification, hidden-information assertion results, step results, diagnostics, and final verdict.

Current reports also include smoke run id, seed mode/results, resource compatibility checks, backend precheck, match id strategy, test results path, Unity log path, build validation summary, retry count, environment fields, and command selection reason.

## Repeatability

When enabled, `UnityClientLiveBackendRepeatabilityTests` runs two public API flows with `SeedMode.Ensure`. The second run must not fail because seed resources already exist, and it must use a distinct match id and verify replay.

The production gameplay shell pass keeps this smoke as a regression gate. Shell/view-model changes must not weaken command submission, event monotonicity, reconnect, replay, or hidden-information assertions.

## Expected Pass Conditions

- Health and capabilities pass.
- Deterministic demo seed succeeds.
- Player and spectator sessions are created.
- Match starts and all projections are fetched.
- Hidden-information assertions pass.
- A server-provided legal command is submitted.
- Events are polled and event sequences are monotonic.
- Reconnect succeeds.
- Replay verification succeeds.

## Known Skip Conditions

- PlayMode test skips when `CARDGAME_UNITY_LIVE_BACKEND_TESTS` is not `1`.
- Scripts report an environment skip when `UNITY_EDITOR` is missing or invalid.
- The full enabled smoke cannot pass unless the backend is already running.

## Troubleshooting

- Run `bash scripts/unity/run-local-smoke.sh` first to verify backend data and public match flow.
- Check Unity batchmode logs under `artifacts/unity-test-logs/`.
- Check full smoke reports under `artifacts/unity-client-live-smoke/`.
- If replay verification fails, treat the smoke as failed evidence and inspect report diagnostics.

## Current Limitations

The smoke is a development evidence path, not production matchmaking. It uses deterministic debug data, built-in Unity UI, no production art/audio, and no local gameplay rules.
