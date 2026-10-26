# Unity Generated Contracts

The repository uses a hand-maintained Unity-facing C# contracts package:

`src/Game.UnityClient.Contracts/`

OpenAPI code generation was not used in this pass because the repository did not already carry a deterministic generator, and the immediate Unity need is a small dependency-free DTO package that can compile as `netstandard2.1` and be copied into Unity or referenced as a DLL.

## Package Rules

- No references to `Game.Domain`, `Game.Application`, `Game.Api`, ASP.NET, storage, generator, EF, or replay internals.
- DTOs are plain classes with public get/set properties.
- Enum-like fields remain strings so unknown future values do not break deserialization.
- JSON examples under `docs/unity/examples/` deserialize into the package in integration tests.
- OpenAPI schema names are compared to DTO type names in integration tests.

## Build And Verify

```bash
dotnet build src/Game.UnityClient.Contracts/Game.UnityClient.Contracts.csproj --configuration Release
dotnet test tests/Game.Integration.Tests/Game.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~UnityContractDocumentationTests
```

Unity should use camelCase JSON naming and ignore unknown fields. With `System.Text.Json`, use `JsonSerializerDefaults.Web` and `JsonStringEnumConverter` only if the client adds local enums.

## Import Into Unity

Use one of:

- reference the built DLL from `src/Game.UnityClient.Contracts/bin/Release/netstandard2.1/`;
- copy `UnityApiDtos.cs` into a Unity assembly definition dedicated to backend contracts.

The Unity source mirror is synchronized deterministically with:

```bash
bash scripts/unity/sync-unity-dtos.sh
bash scripts/unity/check-unity-dto-drift.sh
```

The sync transform removes only Unity-incompatible `System.Text.Json` extension-data pieces and converts the file-scoped namespace to a block-scoped namespace for the current Unity C# language level. If a deterministic OpenAPI generator is introduced later, it must preserve the same package boundary, mirror drift check, and test guarantees.
