using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class UpdateQuestProgressMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "UPDATE_QUEST_PROGRESS";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && mutation.Payload.TryGetValue("amount", out var amount)
      && amount > 0
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && (player.Quests ?? []).Any(quest =>
        StringComparer.Ordinal.Equals(quest.QuestId, metadata.QuestId)
        && !quest.IsCompleted
        && quest.RequiredProgress > 0);
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata)
      || !mutation.Payload.TryGetValue("amount", out var amount))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing quest progress metadata.");
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

    return mutableState.WithQuestProgressChanged(metadata.PlayerId, metadata.QuestId, amount);
  }

  private static bool TryReadMetadata(StateMutation mutation, out QuestProgressMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("questId", out var questId))
    {
      return false;
    }

    metadata = new QuestProgressMetadata(playerId, questId);
    return true;
  }

  private readonly record struct QuestProgressMetadata(string PlayerId, string QuestId);
}
