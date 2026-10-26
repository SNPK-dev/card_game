# Unity Client Release Candidate

## Status

Verdict: `unity-client-release-candidate-ready-with-known-limitations`.

The current Unity client is release-candidate ready for local/editor, standalone, and WebGL gameplay validation using generated/repo-owned visuals/audio. It is not a final commercial launch package because final commissioned media, production hosting, matchmaking, accounts, and shop flows are out of scope.

## Run RC Validation

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
bash scripts/unity/run-unity-client-rc-validation.sh
```

## Build RC Players

```bash
bash scripts/unity/run-unity-release-candidate-build.sh --standalone-rc
bash scripts/unity/run-unity-release-candidate-build.sh --webgl-rc
```

Outputs are ignored under `artifacts/unity-builds/`. Summaries are written under `artifacts/unity-build-logs/<timestamp>/build-summary.json`.

## Full Live Smoke

```bash
CARDGAME_UNITY_LIVE_BACKEND_TESTS=1 bash scripts/unity/run-unity-playmode-smoke.sh
```

This remains the backend gameplay proof and includes the repeatability proof.

## MCP Inspection

Use `docs/unity/mcp-inspection.md`. MCP is the interactive editor inspection layer and does not replace batchmode validation.

## Playtest Evidence

Review local reports under ignored `artifacts/unity-client-playtest/` and sanitized examples under `docs/unity/examples/`. If no manual tester notes exist, evidence must explicitly say notes were not provided.

## Go/No-Go Checklist

- RC validation passes.
- Full live PlayMode smoke and repeatability proof pass.
- Standalone and WebGL builds pass or have exact non-product skip reasons.
- MCP final inspection passes when an active editor instance is available.
- Media inventory gate passes.
- Quality/performance/readability gate passes.
- Authority and hidden-info gate passes.
- Playtest evidence exists.
- Production readiness audit says `release-candidate-ready-with-known-limitations`.
- No build output or raw artifact is staged.

## Known Limitations

- Generated/in-repo placeholder visuals/audio remain.
- No live service deployment, matchmaking, accounts, shop, or production hosting.
- WebGL hosting/CORS deployment is separate from local WebGL build proof.
- Formal accessibility review and full profiler budgets remain future work.
