using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class AdvanceTurnMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "ADVANCE_TURN";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && !string.IsNullOrWhiteSpace(state.ActivePlayerId)
      && StringComparer.Ordinal.Equals(state.ActivePlayerId, metadata.ActivePlayerId)
      && state.Players.ContainsKey(metadata.ActivePlayerId);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out _))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing turn advancement metadata.");
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

    return mutableState.WithTurnAdvanced();
  }

  private static bool TryReadMetadata(
    StateMutation mutation,
    out AdvanceTurnMetadata metadata)
  {
    metadata = default;
    if (mutation.Metadata is null
      || !mutation.Metadata.TryGetValue("activePlayerId", out var activePlayerId)
      || string.IsNullOrWhiteSpace(activePlayerId))
    {
      return false;
    }

    metadata = new AdvanceTurnMetadata(activePlayerId);
    return true;
  }

  private readonly record struct AdvanceTurnMetadata(string ActivePlayerId);
}
