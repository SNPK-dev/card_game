using Game.Domain.State;

namespace Game.Domain.Targeting;

public interface ITargetResolver
{
  TargetResolutionResult Resolve(IGameState state, TargetResolutionRequest request);
}
