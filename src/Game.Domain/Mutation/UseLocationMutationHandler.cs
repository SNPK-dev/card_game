using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class UseLocationMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "USE_LOCATION";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return mutation.TargetRef is not null
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount > 0
      && mutation.Metadata is not null
      && mutation.Metadata.TryGetValue("playerId", out var playerId)
      && mutation.Metadata.TryGetValue("locationEntityInstanceId", out var locationEntityInstanceId)
      && state.Players.TryGetValue(playerId, out var player)
      && player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, locationEntityInstanceId);
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
