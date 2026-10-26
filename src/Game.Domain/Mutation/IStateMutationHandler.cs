using Game.Domain.State;

namespace Game.Domain.Mutation;

public interface IStateMutationHandler
{
  string MutationType { get; }

  bool CanApply(IGameState state, StateMutation mutation);

  IGameState Apply(IGameState state, StateMutation mutation);
}
