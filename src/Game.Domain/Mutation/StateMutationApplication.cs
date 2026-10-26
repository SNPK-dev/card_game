using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed record StateMutationApplication(
  IGameState State,
  AppliedMutationBatch AppliedBatch);
