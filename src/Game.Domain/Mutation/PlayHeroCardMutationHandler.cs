using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class PlayHeroCardMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "PLAY_HERO_CARD";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.ContainsCardInZone(metadata.CardInstanceId, Game.Shared.Primitives.SourceZone.SetAside)
      && metadata.ArmorGain >= 0
      && (!metadata.HeroPowerCost.HasValue || metadata.HeroPowerCost.Value >= 0);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing hero card metadata.");
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

    var heroPower = metadata.HeroPowerCost.HasValue
      ? new HeroPowerState(
        metadata.CardInstanceId,
        metadata.HeroPowerCost.Value,
        metadata.HeroPowerActivationTiming)
      : null;

    return mutableState.WithHeroCardPlayed(
      metadata.PlayerId,
      metadata.ArmorGain,
      heroPower);
  }

  private static bool TryReadMetadata(StateMutation mutation, out PlayHeroCardMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("cardInstanceId", out var cardInstanceId)
      || !mutation.Payload.TryGetValue("armorGain", out var armorGain))
    {
      return false;
    }

    int? heroPowerCost = null;
    if (mutation.Payload.TryGetValue("heroPowerCost", out var parsedHeroPowerCost))
    {
      heroPowerCost = parsedHeroPowerCost;
    }

    var heroPowerActivationTiming = values.TryGetValue("heroPowerActivationTiming", out var timing)
      ? timing
      : string.Empty;
    if (heroPowerCost.HasValue && string.IsNullOrWhiteSpace(heroPowerActivationTiming))
    {
      return false;
    }

    metadata = new PlayHeroCardMetadata(
      playerId,
      cardInstanceId,
      armorGain,
      heroPowerCost,
      heroPowerActivationTiming);
    return true;
  }

  private readonly record struct PlayHeroCardMetadata(
    string PlayerId,
    string CardInstanceId,
    int ArmorGain,
    int? HeroPowerCost,
    string HeroPowerActivationTiming);
}
