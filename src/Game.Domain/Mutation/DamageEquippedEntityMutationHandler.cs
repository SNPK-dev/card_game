using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class DamageEquippedEntityMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "DAMAGE_EQUIPPED_ENTITY";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount > 0
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && IsMatchingEquippedEntity(player, metadata.EntityInstanceId);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing equipped entity damage metadata.");
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

    return mutableState.WithEquippedEntityDamageChanged(
      metadata.PlayerId,
      metadata.EntityInstanceId,
      mutation.Payload["amount"]);
  }

  private static bool TryReadMetadata(StateMutation mutation, out EquippedEntityMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId))
    {
      return false;
    }

    metadata = new EquippedEntityMetadata(playerId, entityInstanceId);
    return true;
  }

  private static bool IsMatchingEquippedEntity(
    PlayerState player,
    string entityInstanceId) =>
    player.Weapon is not null
      && StringComparer.Ordinal.Equals(player.Weapon.EntityInstanceId, entityInstanceId)
    || player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, entityInstanceId);

  private readonly record struct EquippedEntityMetadata(
    string PlayerId,
    string EntityInstanceId);
}
