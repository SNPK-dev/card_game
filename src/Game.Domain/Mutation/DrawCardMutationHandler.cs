using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class DrawCardMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "DRAW_CARD";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.ContainsCardInZone(metadata.CardInstanceId, SourceZone.Deck)
      && IsDestinationValid(player, metadata);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing draw card metadata.");
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
      SourceZone.Deck,
      metadata.DestinationZone);
  }

  private static bool TryReadMetadata(StateMutation mutation, out DrawCardMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("cardInstanceId", out var cardInstanceId))
    {
      return false;
    }

    if (!TryReadOptionalLimit(values, "maxHandSize", out var maxHandSize))
    {
      return false;
    }

    if (!TryReadDestination(values, out var destinationZone))
    {
      return false;
    }

    metadata = new DrawCardMetadata(playerId, cardInstanceId, maxHandSize, destinationZone);
    return true;
  }

  private static bool TryReadDestination(
    IReadOnlyDictionary<string, string> values,
    out SourceZone destinationZone)
  {
    destinationZone = SourceZone.Hand;
    if (!values.TryGetValue("destinationZone", out var value))
    {
      return true;
    }

    destinationZone = value switch
    {
      "HAND" => SourceZone.Hand,
      "GRAVEYARD" => SourceZone.Graveyard,
      _ => default
    };
    return value is "HAND" or "GRAVEYARD";
  }

  private static bool IsDestinationValid(PlayerState player, DrawCardMetadata metadata) =>
    metadata.DestinationZone switch
    {
      SourceZone.Hand => !metadata.MaxHandSize.HasValue || player.Zones.Hand.Count < metadata.MaxHandSize.Value,
      SourceZone.Graveyard => metadata.MaxHandSize.HasValue && player.Zones.Hand.Count >= metadata.MaxHandSize.Value,
      _ => false
    };

  private static bool TryReadOptionalLimit(
    IReadOnlyDictionary<string, string> values,
    string key,
    out int? limit)
  {
    limit = null;
    if (!values.TryGetValue(key, out var value))
    {
      return true;
    }

    if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
      || parsed < 0)
    {
      return false;
    }

    limit = parsed;
    return true;
  }

  private readonly record struct DrawCardMetadata(
    string PlayerId,
    string CardInstanceId,
    int? MaxHandSize,
    SourceZone DestinationZone);
}
