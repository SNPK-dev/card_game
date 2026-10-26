using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class ReplaceHeroPowerMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "REPLACE_HERO_POWER";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.ContainsCardInZone(metadata.CardInstanceId, SourceZone.SetAside)
      && metadata.HeroPowerCost >= 0
      && !string.IsNullOrWhiteSpace(metadata.HeroPowerActivationTiming);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing hero power replacement metadata.");
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

    return mutableState.WithHeroPowerReplaced(
      metadata.PlayerId,
      new HeroPowerState(
        metadata.CardInstanceId,
        metadata.HeroPowerCost,
        metadata.HeroPowerActivationTiming));
  }

  private static bool TryReadMetadata(StateMutation mutation, out ReplaceHeroPowerMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("cardInstanceId", out var cardInstanceId)
      || !values.TryGetValue("heroPowerActivationTiming", out var heroPowerActivationTiming)
      || !mutation.Payload.TryGetValue("heroPowerCost", out var heroPowerCost))
    {
      return false;
    }

    metadata = new ReplaceHeroPowerMetadata(
      playerId,
      cardInstanceId,
      heroPowerCost,
      heroPowerActivationTiming);
    return true;
  }

  private readonly record struct ReplaceHeroPowerMetadata(
    string PlayerId,
    string CardInstanceId,
    int HeroPowerCost,
    string HeroPowerActivationTiming);
}

