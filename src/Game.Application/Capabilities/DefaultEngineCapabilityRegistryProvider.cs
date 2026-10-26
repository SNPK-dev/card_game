using Game.Shared.Engine;

namespace Game.Application.Capabilities;

public sealed class DefaultEngineCapabilityRegistryProvider : IEngineCapabilityRegistryProvider
{
  private static readonly EngineCapabilityRegistryBuildResult CurrentBuild = new RuntimeEngineCapabilityRegistryBuilder().Build(
    "engine-bootstrap-0.3.0",
    DefaultRuntimeCapabilityDeclarations.Create(),
    new EngineCapabilityMetadata(
      "runtime-capability-builder",
      DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
      "runtime-capability-builder",
      DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
      [
        "docs/engine/main.md",
        "docs/generator/ruleset_profile_spec.md",
        "openspec/specs/application-api-infrastructure/spec.md",
        "openspec/specs/shared-contracts/spec.md"
      ],
      [
        "Capability registry is aggregated from explicit runtime support declarations.",
        "Startup validation rejects duplicate mutation capabilities and missing committed event mappings."
      ]));

  public EngineCapabilityRegistry GetCurrentRegistry() => CurrentBuild.Registry;

  public string GetCurrentRegistryHash() => CurrentBuild.RegistryHash;
}
