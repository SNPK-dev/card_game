using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class ReturnEntityToHandMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "RETURN_ENTITY_TO_HAND";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.Board.Any(entity =>
        StringComparer.Ordinal.Equals(entity.EntityInstanceId, metadata.EntityInstanceId)
        && player.Zones.ContainsCardInZone(entity.SourceCardInstanceId, SourceZone.SetAside));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing return-to-hand metadata.");
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

    return mutableState.WithBoardEntityReturnedToHand(
      metadata.PlayerId,
      metadata.EntityInstanceId);
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

  private readonly record struct EntityMutationMetadata(string PlayerId, string EntityInstanceId);
}
