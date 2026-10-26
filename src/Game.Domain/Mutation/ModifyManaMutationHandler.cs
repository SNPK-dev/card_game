using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ModifyManaMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "MODIFY_MANA";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var playerId)
      && mutation.Payload.TryGetValue("amount", out var amount)
      && state.Players.TryGetValue(playerId, out var player)
      && player.Mana + amount >= 0;
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var playerId))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing mana metadata.");
    }

    if (!mutation.Payload.TryGetValue("amount", out var _))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing mana amount payload.");
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
    var player = mutableState.Players[playerId];

    return mutableState.WithPlayer(player with
    {
      Mana = player.Mana + mutation.Payload["amount"]
    });
  }

  private static bool TryReadMetadata(StateMutation mutation, out string playerId)
  {
    playerId = string.Empty;
    if (mutation.Metadata is null
      || !mutation.Metadata.TryGetValue("playerId", out var value)
      || string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    playerId = value;
    return true;
  }
}
