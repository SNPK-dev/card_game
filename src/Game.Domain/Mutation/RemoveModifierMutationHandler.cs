using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class RemoveModifierMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "REMOVE_MODIFIER";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Modifiers.Any(modifier =>
        StringComparer.Ordinal.Equals(modifier.ModifierId, metadata.ModifierId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing remove modifier metadata.");
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

    return mutableState.WithModifierRemoved(metadata.PlayerId, metadata.ModifierId);
  }

  private static bool TryReadMetadata(StateMutation mutation, out RemoveModifierMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("modifierId", out var modifierId))
    {
      return false;
    }

    metadata = new RemoveModifierMetadata(playerId, modifierId);
    return true;
  }

  private readonly record struct RemoveModifierMetadata(string PlayerId, string ModifierId);
}
