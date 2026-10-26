using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class CreateLocationMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "CREATE_LOCATION";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Location is null
      && player.Zones.ContainsCardInZone(metadata.SourceCardInstanceId, SourceZone.SetAside)
      && metadata.Durability > 0;
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing create location metadata.");
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

    return mutableState.WithLocationPlayed(
      metadata.PlayerId,
      new EntityInstance(
        metadata.EntityInstanceId,
        metadata.SourceCardInstanceId,
        metadata.PlayerId,
        EntityType.Location,
        Attack: 0,
        Health: metadata.Durability,
        Damage: 0));
  }

  private static bool TryReadMetadata(StateMutation mutation, out CreateLocationMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId)
      || !mutation.Payload.TryGetValue("durability", out var durability))
    {
      return false;
    }

    metadata = new CreateLocationMetadata(
      playerId,
      entityInstanceId,
      sourceCardInstanceId,
      durability);
    return true;
  }

  private readonly record struct CreateLocationMetadata(
    string PlayerId,
    string EntityInstanceId,
    string SourceCardInstanceId,
    int Durability);
}
