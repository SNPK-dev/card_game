using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ModifyHealthMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "MODIFY_HEALTH";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount > 0
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.Board.Any(entity =>
        StringComparer.Ordinal.Equals(entity.EntityInstanceId, metadata.EntityInstanceId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata)
      || !mutation.Payload.TryGetValue("amount", out var amount))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing modify health metadata.");
    }

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

    return mutableState.WithEntityHealthChanged(metadata.PlayerId, metadata.EntityInstanceId, amount);
  }

  private static bool TryReadMetadata(StateMutation mutation, out ModifyHealthMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId))
    {
      return false;
    }

    metadata = new ModifyHealthMetadata(playerId, entityInstanceId);
    return true;
  }

  private readonly record struct ModifyHealthMetadata(string PlayerId, string EntityInstanceId);
}
