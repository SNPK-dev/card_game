using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ApplyModifierMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "APPLY_MODIFIER";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount > 0
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && !player.Modifiers.Any(modifier =>
        StringComparer.Ordinal.Equals(modifier.ModifierId, metadata.ModifierId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata)
      || !mutation.Payload.TryGetValue("amount", out var amount))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing apply modifier metadata.");
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

    return mutableState.WithModifierApplied(
      metadata.PlayerId,
      new PlayerModifier(
        metadata.ModifierId,
        metadata.ModifierType,
        amount,
        metadata.Duration,
        metadata.Priority,
        metadata.SourceRef));
  }

  private static bool TryReadMetadata(StateMutation mutation, out ApplyModifierMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("modifierId", out var modifierId)
      || !values.TryGetValue("modifierType", out var modifierType)
      || !values.TryGetValue("duration", out var duration))
    {
      return false;
    }

    if (!IsSupportedModifier(modifierType, duration))
    {
      return false;
    }

    if (values.TryGetValue("priority", out var priorityValue)
      && !int.TryParse(priorityValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
    {
      return false;
    }

    var priority = values.TryGetValue("priority", out priorityValue)
      ? int.Parse(priorityValue, System.Globalization.CultureInfo.InvariantCulture)
      : 0;
    var sourceRef = values.TryGetValue("sourceRef", out var source)
      ? source
      : mutation.SourceRef;

    metadata = new ApplyModifierMetadata(playerId, modifierId, modifierType, duration, priority, sourceRef);
    return true;
  }

  private static bool IsSupportedModifier(string modifierType, string duration) =>
    (StringComparer.Ordinal.Equals(modifierType, PlayerModifierTypes.CostModifier)
      && (StringComparer.Ordinal.Equals(duration, PlayerModifierDurations.NextPlay)
        || StringComparer.Ordinal.Equals(duration, PlayerModifierDurations.RestOfTurn)))
    || (StringComparer.Ordinal.Equals(modifierType, PlayerModifierTypes.DamagePrevention)
      && StringComparer.Ordinal.Equals(duration, PlayerModifierDurations.NextDamage));

  private readonly record struct ApplyModifierMetadata(
    string PlayerId,
    string ModifierId,
    string ModifierType,
    string Duration,
    int Priority,
    string SourceRef);
}
