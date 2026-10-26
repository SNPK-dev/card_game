using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class MoveCardZoneMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "MOVE_CARD_ZONE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.ContainsCardInZone(metadata.CardInstanceId, metadata.FromZone);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing card zone movement metadata.");
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

    return mutableState.WithCardMoved(
      metadata.PlayerId,
      metadata.CardInstanceId,
      metadata.FromZone,
      metadata.ToZone);
  }

  private static bool TryReadMetadata(
    StateMutation mutation,
    out MoveCardZoneMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("cardInstanceId", out var cardInstanceId)
      || !values.TryGetValue("fromZone", out var fromZoneValue)
      || !values.TryGetValue("toZone", out var toZoneValue)
      || !TryParseZone(fromZoneValue, out var fromZone)
      || !TryParseZone(toZoneValue, out var toZone))
    {
      return false;
    }

    metadata = new MoveCardZoneMetadata(playerId, cardInstanceId, fromZone, toZone);
    return true;
  }

  private static bool TryParseZone(string value, out SourceZone zone)
  {
    zone = value switch
    {
      "DECK" => SourceZone.Deck,
      "HAND" => SourceZone.Hand,
      "GRAVEYARD" => SourceZone.Graveyard,
      "SET_ASIDE" => SourceZone.SetAside,
      _ => default
    };

    return value is "DECK" or "HAND" or "GRAVEYARD" or "SET_ASIDE";
  }

  private readonly record struct MoveCardZoneMetadata(
    string PlayerId,
    string CardInstanceId,
    SourceZone FromZone,
    SourceZone ToZone);
}
