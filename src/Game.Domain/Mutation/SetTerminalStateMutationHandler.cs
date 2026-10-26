using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class SetTerminalStateMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "SET_TERMINAL_STATE";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return !state.TerminalState.IsTerminal
      && TryReadMetadata(mutation, out var metadata)
      && !string.IsNullOrWhiteSpace(metadata.Reason);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing terminal state metadata.");
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

    return mutableState.WithTerminalState(new GameTerminalState(
      IsTerminal: true,
      metadata.Reason,
      metadata.WinningPlayerId,
      metadata.LosingPlayerId,
      metadata.Message));
  }

  private static bool TryReadMetadata(
    StateMutation mutation,
    out TerminalMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("reason", out var reason)
      || string.IsNullOrWhiteSpace(reason))
    {
      return false;
    }

    values.TryGetValue("winningPlayerId", out var winningPlayerId);
    values.TryGetValue("losingPlayerId", out var losingPlayerId);
    values.TryGetValue("message", out var message);
    metadata = new TerminalMetadata(
      reason,
      string.IsNullOrWhiteSpace(winningPlayerId) ? null : winningPlayerId,
      string.IsNullOrWhiteSpace(losingPlayerId) ? null : losingPlayerId,
      message ?? string.Empty);
    return true;
  }

  private readonly record struct TerminalMetadata(
    string Reason,
    string? WinningPlayerId,
    string? LosingPlayerId,
    string Message);
}
