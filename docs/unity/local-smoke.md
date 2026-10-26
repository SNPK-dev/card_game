# Unity Local Smoke

## Local Backend

```bash
dotnet run --project src/Game.Api
```

Default local URL is `http://localhost:5000`. Use `Storage:RootPath` to choose persistent JSON storage. Docker instructions are in `../deployment/container-stack.md`.

## One-Command Smoke

```bash
bash scripts/unity/run-local-smoke.sh
```

The script starts or uses a local backend, checks health, stores demo data, creates sessions, starts a match, fetches state/legal actions, submits one command, polls events, reconnects, and verifies replay.

## Two-Bot Smoke

```bash
bash scripts/unity/run-bot-match-smoke.sh
```

This runs the reference headless two-bot flow through public HTTP APIs and writes a report under `artifacts/unity-integration-smoke/`.

The bot intentionally concedes after its non-concede step limit so the smoke reaches a deterministic terminal state. A committed representative report is `examples/smoke-report.example.json`.

## WebGL CORS Smoke

```bash
bash scripts/unity/run-webgl-cors-smoke.sh
```

This validates configured CORS preflight behavior for browser-hosted Unity clients.

## Unity Local Match Scene

Open `clients/unity/CardGame.Unity` and load `Assets/Scenes/CardGameLocalMatch.unity`. The scene contains `CardGameClientRoot` and the local debug bootstrapper. See `client-local-match.md` for the full manual playthrough.

## Guarded Unity Smoke

```bash
CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
bash scripts/unity/run-unity-dev-build-smoke.sh
```

Both scripts require `UNITY_EDITOR` for batchmode execution. Without it, they report the environment blocker and do not create build output.

The full live Unity smoke report is written under `artifacts/unity-client-live-smoke/`. See `client-full-live-smoke.md`.

On Windows through WSL, set `UNITY_EDITOR` with the mounted path form, for example:

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
```

The 2026-07-08 enabled PlayMode smoke passed and sanitized evidence is committed at `examples/unity-live-smoke-report.actual-sanitized.json`.
