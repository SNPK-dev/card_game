using Game.Domain.Commands;
using Game.Domain.State;

namespace Game.Domain.Mutation;

public interface IStateMutator
{
  IGameState Apply(IGameState state, IGameCommand command);

  StateMutationApplication Apply(IGameState state, ProposedMutationBatch batch);
}
