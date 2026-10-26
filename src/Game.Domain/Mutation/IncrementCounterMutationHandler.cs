using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class IncrementCounterMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "INCREMENT_COUNTER";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return mutation.TargetRef is not null
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount >= 0;
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!CanApply(state, mutation))
    {
      throw new InvalidOperationException(
        $"Cannot apply mutation '{mutation.MutationId}' of type '{mutation.MutationType}'.");
    }

    var mutableState = state as InMemoryGameState
      ?? new InMemoryGameState(
        state.Revision,
        state.Counters,
        state.Players,
        state.PresentationMetadata,
        state.ActivePlayerId,
        state.TurnNumber,
        state.Phase,
        state.TerminalState,
        state.PendingChoices);

    var targetKey = mutation.TargetRef!;
    state.Counters.TryGetValue(targetKey, out var currentValue);
    return mutableState.WithCounter(targetKey, currentValue + mutation.Payload["amount"]);
  }
}
