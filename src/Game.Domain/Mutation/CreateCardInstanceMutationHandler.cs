using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class CreateCardInstanceMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "CREATE_CARD_INSTANCE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.ContainsKey(metadata.PlayerId)
      && !state.Players.Values.Any(player => PlayerHasCard(player, metadata.CardInstanceId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing create card instance metadata.");
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

    return mutableState.WithCardCreated(
      metadata.PlayerId,
      new CardInstance(
        metadata.CardInstanceId,
        metadata.CardId,
        metadata.CardVersion,
        metadata.PlayerId,
        metadata.CardType),
      metadata.Zone);
  }

  private static bool TryReadMetadata(StateMutation mutation, out CreateCardInstanceMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("cardInstanceId", out var cardInstanceId)
      || !values.TryGetValue("cardId", out var cardId)
      || !values.TryGetValue("cardVersion", out var cardVersion)
      || !values.TryGetValue("cardType", out var cardTypeValue)
      || !values.TryGetValue("toZone", out var zoneValue)
      || string.IsNullOrWhiteSpace(playerId)
      || string.IsNullOrWhiteSpace(cardInstanceId)
      || string.IsNullOrWhiteSpace(cardId)
      || string.IsNullOrWhiteSpace(cardVersion)
      || !TryReadCardType(cardTypeValue, out var cardType)
      || !TryReadZone(zoneValue, out var zone))
    {
      return false;
    }

    metadata = new CreateCardInstanceMetadata(
      playerId,
      cardInstanceId,
      cardId,
      cardVersion,
      cardType,
      zone);
    return true;
  }

  private static bool PlayerHasCard(PlayerState player, string cardInstanceId) =>
    player.Zones.Deck
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.SetAside)
      .Any(card => StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId));

  private static bool TryReadCardType(string value, out CardType cardType)
  {
    cardType = value switch
    {
      "MINION_CARD" => CardType.MinionCard,
      "SPELL_CARD" => CardType.SpellCard,
      "WEAPON_CARD" => CardType.WeaponCard,
      "LOCATION_CARD" => CardType.LocationCard,
      "HERO_CARD" => CardType.HeroCard,
      "HERO_POWER_CARD" => CardType.HeroPowerCard,
      "TOKEN_CARD" => CardType.TokenCard,
      _ => default
    };

    return value is
      "MINION_CARD"
      or "SPELL_CARD"
      or "WEAPON_CARD"
      or "LOCATION_CARD"
      or "HERO_CARD"
      or "HERO_POWER_CARD"
      or "TOKEN_CARD";
  }

  private static bool TryReadZone(string value, out SourceZone zone)
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

  private readonly record struct CreateCardInstanceMetadata(
    string PlayerId,
    string CardInstanceId,
    string CardId,
    string CardVersion,
    CardType CardType,
    SourceZone Zone);
}
