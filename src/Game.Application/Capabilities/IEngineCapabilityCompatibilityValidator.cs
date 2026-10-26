namespace Game.Application.Capabilities;

public interface IEngineCapabilityCompatibilityValidator
{
  EngineCapabilityCompatibilityResult Validate(EngineCapabilityCompatibilityRequest request);
}
