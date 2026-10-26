# Unity Client Contract Import

## Strategy

The first Unity vertical slice uses source import from `src/Game.UnityClient.Contracts/UnityApiDtos.cs` into:

```text
clients/unity/CardGame.Unity/Assets/CardGame/Runtime/Contracts/UnityApiDtos.cs
```

The imported file preserves the `Game.UnityClient.Contracts` namespace and DTO class names so runtime code consumes the prepared public contract instead of backend internals.

## Unity Adaptation

The source DTO package targets `netstandard2.1` and uses `System.Text.Json` extension-data annotations for unknown fields. The Unity copy removes only those `System.Text.Json` extension-data annotations and stores no backend implementation types. Runtime JSON serialization uses Unity-compatible Newtonsoft JSON through `BackendApiClient`.

## Sync Script

From the repository root, refresh the Unity DTO mirror with:

```bash
bash scripts/unity/sync-unity-dtos.sh
```

After syncing, refresh Unity assets and run EditMode tests. The namespace transform is required because the Unity project currently compiles with C# 9.

## Drift Detection

Run:

```bash
bash scripts/unity/check-unity-dto-drift.sh
```

The check applies the same transform to the source DTO file in a temporary location and compares it with the committed Unity mirror. Any contract field, endpoint DTO, or namespace change must be reviewed against `docs/api/openapi.yaml` and `docs/unity/generated-contracts.md`.

## Limitations

- This is a source mirror, not a generated package reference.
- Unknown JSON extension fields are not preserved in the Unity mirror during this pass.
- The Unity client still treats the backend response as authoritative and does not deserialize backend domain/application types.
