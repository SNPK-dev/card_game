using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ApplyDamageMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "APPLY_DAMAGE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && TryReadAmount(mutation, out var amount)
      && amount > 0
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && (player.Zones.ContainsEntityOnBoard(metadata.EntityInstanceId)
        || (player.Hero is not null
          && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, metadata.EntityInstanceId)));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata) || !TryReadAmount(mutation, out var amount))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing damage metadata.");
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

    return mutableState.WithEntityDamageChanged(
      metadata.PlayerId,
      metadata.EntityInstanceId,
      damageDelta: amount);
  }

  private static bool TryReadMetadata(StateMutation mutation, out EntityMutationMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId))
    {
      return false;
    }

    metadata = new EntityMutationMetadata(playerId, entityInstanceId);
    return true;
  }

  private static bool TryReadAmount(StateMutation mutation, out int amount)
  {
    return mutation.Payload.TryGetValue("amount", out amount);
  }

  private readonly record struct EntityMutationMetadata(string PlayerId, string EntityInstanceId);
}
