using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class DestroyEquippedEntityMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "DESTROY_EQUIPPED_ENTITY";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && IsMatchingEquippedEntity(player, metadata.EntityInstanceId, metadata.SourceCardInstanceId);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing equipped entity destroy metadata.");
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

    return mutableState.WithEquippedEntityDestroyed(
      metadata.PlayerId,
      metadata.EntityInstanceId);
  }

  private static bool TryReadMetadata(StateMutation mutation, out EquippedEntityDestroyMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId))
    {
      return false;
    }

    metadata = new EquippedEntityDestroyMetadata(playerId, entityInstanceId, sourceCardInstanceId);
    return true;
  }

  private static bool IsMatchingEquippedEntity(
    PlayerState player,
    string entityInstanceId,
    string sourceCardInstanceId) =>
    player.Weapon is not null
      && StringComparer.Ordinal.Equals(player.Weapon.EntityInstanceId, entityInstanceId)
      && StringComparer.Ordinal.Equals(player.Weapon.SourceCardInstanceId, sourceCardInstanceId)
    || player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, entityInstanceId)
      && StringComparer.Ordinal.Equals(player.Location.SourceCardInstanceId, sourceCardInstanceId);

  private readonly record struct EquippedEntityDestroyMetadata(
    string PlayerId,
    string EntityInstanceId,
    string SourceCardInstanceId);
}
