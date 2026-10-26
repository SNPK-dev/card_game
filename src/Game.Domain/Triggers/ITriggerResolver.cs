namespace Game.Domain.Triggers;

public interface ITriggerResolver
{
  TriggerResolutionResult Resolve(TriggerResolutionRequest request);
}
