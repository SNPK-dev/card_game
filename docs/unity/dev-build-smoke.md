# Unity Development Build Smoke

This smoke is a development validation path, not a production build pipeline.

Batchmode is the repeatable validation source of truth. `unityMCP` is the interactive Unity Editor inspection layer and does not replace this smoke.

## Validation-Only Mode

Run:

```bash
export UNITY_EDITOR="/path/to/Unity"
bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
```

On Windows through WSL:

```bash
export UNITY_EDITOR="/mnt/c/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Unity.exe"
bash scripts/unity/run-unity-dev-build-smoke.sh --validate-only
```

The script invokes `CardGame.Unity.Client.Editor.Build.UnityBuildSmoke.ValidateBatch`, which validates the local match scene/root/build-smoke assumptions without producing build output.

Validation checks the Unity editor path, project path, local match scene, EditorBuildSettings entry, `CardGameClientRoot`, required polished shell child hierarchy, required `Canvas` gameplay regions, required card/board/hero/resource prefabs, audio/presentation/card/board runtime types, missing script references, ignored build output assumptions, and required Unity smoke scripts. Logs are written under `artifacts/unity-build-logs/`.

Script modes:

```bash
bash scripts/unity/run-unity-dev-build-smoke.sh --standalone-dev
bash scripts/unity/run-unity-dev-build-smoke.sh --webgl-dev
bash scripts/unity/run-unity-release-candidate-build.sh --standalone-rc
bash scripts/unity/run-unity-release-candidate-build.sh --webgl-rc
```

These modes invoke Unity build hooks after validation. If the target module is unavailable, Unity logs a clear unsupported-target warning and exits without producing output. If the module is available, build output is written under ignored `artifacts/unity-builds/` and real build errors fail the script.

Every run writes a summary JSON under:

```text
artifacts/unity-build-logs/<timestamp>/build-summary.json
```

The summary records target, mode, Unity version, project path, timing, result, output path, log path, build size if available, scene inclusion, root hierarchy validation, missing scripts, warning/error counts, skip reason, and exit code. For build modes, a passed build must have output at the reported path.

## Development Player Builds

Full standalone or WebGL player output remains environment-dependent. Build output must stay under ignored artifact/build directories and must not be committed.

## Current Status

The validation-only batchmode smoke remains the supported minimum path. Full player build generation depends on installed Unity build target modules, and the summary JSON reports any skip reason.

## MCP Coordination

If `unityMCP` inspection is part of the current handoff, inspect the manually opened Unity Editor first and then close that Editor before running this batchmode smoke. Unity batchmode cannot open `clients/unity/CardGame.Unity` while the same project is already open in the normal editor.

Use `bash scripts/unity/check-unity-mcp-endpoint.sh` only as an endpoint diagnostic. Actual MCP readiness requires tool/resource inspection recorded in `docs/unity/mcp-inspection-latest.md`.
