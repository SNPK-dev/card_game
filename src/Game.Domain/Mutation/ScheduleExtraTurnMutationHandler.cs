using Game.Domain.State;

namespace Game.Domain.Mutation;

public sealed class ScheduleExtraTurnMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "SCHEDULE_EXTRA_TURN";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.ContainsKey(metadata.PlayerId)
      && (!metadata.OncePerGame || !ExtraTurnSchedule.HasOncePerGameUse(state, metadata.PlayerId));
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing extra-turn scheduling metadata.");
    }

    if (!CanApply(state, mutation))
    {
      throw new InvalidOperationException(
        $"Cannot apply mutation '{mutation.MutationId}' of type '{mutation.MutationType}'.");
    }

    var counters = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var counter in state.Counters)
    {
      counters[counter.Key] = counter.Value;
    }

    counters[metadata.QueueKey] = 1;
    if (metadata.OncePerGame)
    {
      counters[ExtraTurnSchedule.UsedKey(metadata.PlayerId)] = 1;
    }

    return new InMemoryGameState(
      state.Revision + 1,
      counters,
      state.Players,
      state.PresentationMetadata,
      state.ActivePlayerId,
      state.TurnNumber,
      state.Phase,
      state.TerminalState,
      state.PendingChoices);
  }

  private static bool TryReadMetadata(StateMutation mutation, out ExtraTurnMetadata metadata)
  {
    metadata = default;
    if (mutation.Metadata is null
      || !mutation.Metadata.TryGetValue("playerId", out var playerId)
      || string.IsNullOrWhiteSpace(playerId)
      || !mutation.Metadata.TryGetValue("queueKey", out var queueKey)
      || string.IsNullOrWhiteSpace(queueKey))
    {
      return false;
    }

    var oncePerGame = mutation.Metadata.TryGetValue("oncePerGame", out var onceValue)
      && bool.TryParse(onceValue, out var parsed)
      && parsed;
    metadata = new ExtraTurnMetadata(playerId, queueKey, oncePerGame);
    return true;
  }

  private readonly record struct ExtraTurnMetadata(string PlayerId, string QueueKey, bool OncePerGame);
}
