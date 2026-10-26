using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class AddKeywordMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "ADD_KEYWORD";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && player.Zones.Board.Any(entity =>
        StringComparer.Ordinal.Equals(entity.EntityInstanceId, metadata.EntityInstanceId)
        && !(entity.Keywords ?? []).Contains(metadata.Keyword, StringComparer.Ordinal));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing add keyword metadata.");
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

    return mutableState.WithEntityKeywordAdded(metadata.PlayerId, metadata.EntityInstanceId, metadata.Keyword);
  }

  private static bool TryReadMetadata(StateMutation mutation, out AddKeywordMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !values.TryGetValue("keyword", out var keyword)
      || string.IsNullOrWhiteSpace(keyword))
    {
      return false;
    }

    metadata = new AddKeywordMetadata(playerId, entityInstanceId, keyword);
    return true;
  }

  private readonly record struct AddKeywordMetadata(
    string PlayerId,
    string EntityInstanceId,
    string Keyword);
}
