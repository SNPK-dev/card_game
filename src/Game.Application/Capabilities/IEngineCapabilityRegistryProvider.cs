using Game.Shared.Engine;

namespace Game.Application.Capabilities;

public interface IEngineCapabilityRegistryProvider
{
  EngineCapabilityRegistry GetCurrentRegistry();

  string GetCurrentRegistryHash();
}
