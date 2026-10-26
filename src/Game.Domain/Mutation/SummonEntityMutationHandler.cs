using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Mutation;

public sealed class SummonEntityMutationHandler : IStateMutationHandler
{
  public const string SupportedMutationType = "SUMMON_ENTITY";

  public string MutationType => SupportedMutationType;

  public bool CanApply(IGameState state, StateMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(mutation);

    return TryReadMetadata(mutation, out var metadata)
      && state.Players.TryGetValue(metadata.PlayerId, out var player)
      && !player.Zones.ContainsEntityOnBoard(metadata.EntityInstanceId)
      && (!metadata.MaxBoardSize.HasValue || player.Zones.Board.Count < metadata.MaxBoardSize.Value)
      && metadata.Attack >= 0
      && metadata.Health > 0;
  }

  public IGameState Apply(IGameState state, StateMutation mutation)
  {
    if (!TryReadMetadata(mutation, out var metadata))
    {
      throw new InvalidOperationException(
        $"Mutation '{mutation.MutationId}' is missing summon entity metadata.");
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

    return mutableState.WithBoardEntitySummoned(
      metadata.PlayerId,
      new EntityInstance(
        metadata.EntityInstanceId,
        metadata.SourceCardInstanceId,
        metadata.PlayerId,
        metadata.EntityType,
        metadata.Attack,
        metadata.Health,
        Damage: 0,
        metadata.Keywords));
  }

  private static bool TryReadMetadata(StateMutation mutation, out SummonEntityMetadata metadata)
  {
    metadata = default;
    var values = mutation.Metadata;
    if (values is null
      || !values.TryGetValue("playerId", out var playerId)
      || !values.TryGetValue("entityInstanceId", out var entityInstanceId)
      || !values.TryGetValue("sourceCardInstanceId", out var sourceCardInstanceId)
      || !mutation.Payload.TryGetValue("attack", out var attack)
      || !mutation.Payload.TryGetValue("health", out var health)
      || !TryReadEntityType(values, out var entityType))
    {
      return false;
    }

    if (!TryReadOptionalLimit(values, "maxBoardSize", out var maxBoardSize))
    {
      return false;
    }

    metadata = new SummonEntityMetadata(
      playerId,
      entityInstanceId,
      sourceCardInstanceId,
      entityType,
      attack,
      health,
      ReadKeywords(values),
      maxBoardSize);
    return true;
  }

  private static bool TryReadOptionalLimit(
    IReadOnlyDictionary<string, string> values,
    string key,
    out int? limit)
  {
    limit = null;
    if (!values.TryGetValue(key, out var value))
    {
      return true;
    }

    if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
      || parsed < 0)
    {
      return false;
    }

    limit = parsed;
    return true;
  }

  private static IReadOnlyList<string> ReadKeywords(IReadOnlyDictionary<string, string> values) =>
    values
      .Where(pair => pair.Key.StartsWith("entityKeyword:", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(pair.Value))
      .OrderBy(pair => pair.Key, StringComparer.Ordinal)
      .Select(pair => pair.Value)
      .Distinct(StringComparer.Ordinal)
      .ToArray();

  private static bool TryReadEntityType(
    IReadOnlyDictionary<string, string> values,
    out EntityType entityType)
  {
    entityType = values.TryGetValue("entityType", out var value) && value == "MINION"
      ? EntityType.Minion
      : default;

    return values.TryGetValue("entityType", out value) && value == "MINION";
  }

  private readonly record struct SummonEntityMetadata(
    string PlayerId,
    string EntityInstanceId,
    string SourceCardInstanceId,
    EntityType EntityType,
    int Attack,
    int Health,
    IReadOnlyList<string> Keywords,
    int? MaxBoardSize);
}
