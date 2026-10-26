using Game.Shared.Engine;

namespace Game.Application.Capabilities;

public sealed record EngineCapabilityRegistryBuildResult(
  EngineCapabilityRegistry Registry,
  string RegistryHash);
