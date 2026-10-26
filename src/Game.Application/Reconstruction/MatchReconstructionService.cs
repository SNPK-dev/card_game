using Game.Shared.Events;

namespace Game.Application.Reconstruction;

public sealed class MatchReconstructionService
{
  public MatchReconstructionReport Reconstruct(MatchReconstructionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.CommandLog);
    ArgumentNullException.ThrowIfNull(request.RegeneratedEvents);
    ArgumentNullException.ThrowIfNull(request.FinalState);

    var eventsByCommand = request.RegeneratedEvents
      .Where(committedEvent => committedEvent.CommandSeq is not null)
      .GroupBy(committedEvent => committedEvent.CommandSeq!.Value)
      .ToDictionary(
        group => group.Key,
        group => group.OrderBy(committedEvent => committedEvent.EventSeq).ToArray(),
        EqualityComparer<long>.Default);

    var turns = new List<ReconstructedTurn>();
    var currentTurnNumber = 0;
    string? currentActivePlayerId = null;
    var currentCommands = new List<ReconstructedCommand>();

    foreach (var payload in request.CommandLog.Payloads.OrderBy(payload => payload.CommandSeq))
    {
      if (payload.CommandType == "END_TURN" && currentCommands.Count > 0)
      {
        turns.Add(new ReconstructedTurn(currentTurnNumber, currentActivePlayerId, currentCommands.ToArray()));
        currentCommands.Clear();
      }

      currentTurnNumber = Math.Max(currentTurnNumber, InferTurnNumber(payload.CommandSeq, request.RegeneratedEvents));
      currentActivePlayerId = payload.PlayerId ?? currentActivePlayerId;
      var commandEvents = eventsByCommand.TryGetValue(payload.CommandSeq, out var events)
        ? events
        : [];
      currentCommands.Add(new ReconstructedCommand(
        payload.CommandSeq,
        payload.PlayerId,
        payload.CommandType,
        payload.Payload,
        payload.Refs,
        commandEvents.Select(ToEvent).ToArray(),
        commandEvents.SelectMany(ToDeltas).ToArray()));
    }

    if (currentCommands.Count > 0 || turns.Count == 0)
    {
      turns.Add(new ReconstructedTurn(currentTurnNumber, currentActivePlayerId, currentCommands.ToArray()));
    }

    var unassignedEvents = request.RegeneratedEvents
      .Where(committedEvent => committedEvent.CommandSeq is null)
      .OrderBy(committedEvent => committedEvent.EventSeq)
      .ToArray();
    if (unassignedEvents.Length > 0)
    {
      turns.Add(new ReconstructedTurn(
        currentTurnNumber,
        currentActivePlayerId,
        [
          new ReconstructedCommand(
            0,
            null,
            "ENGINE_DERIVED",
            new SortedDictionary<string, int>(StringComparer.Ordinal),
            new SortedDictionary<string, string>(StringComparer.Ordinal),
            unassignedEvents.Select(ToEvent).ToArray(),
            unassignedEvents.SelectMany(ToDeltas).ToArray())
        ]));
    }

    return new MatchReconstructionReport(
      request.MatchId,
      request.CommandLog.Payloads.Count,
      request.RegeneratedEvents.Count,
      request.FinalStateHash,
      request.FinalState.TerminalState.Reason,
      request.FinalState.TerminalState.WinningPlayerId,
      turns,
      (request.AssetManifests ?? [])
        .OrderBy(manifest => manifest.AssetManifestId, StringComparer.Ordinal)
        .Select(manifest => new ReconstructedAssetRef(
          manifest.AssetManifestId,
          manifest.CardId,
          manifest.CardVersionId,
          manifest.VisualAssetMode.ToString(),
          manifest.ContentHash))
        .ToArray());
  }

  private static int InferTurnNumber(int commandSeq, IReadOnlyList<CommittedEventRecord> events)
  {
    var eventTurn = events
      .Where(committedEvent => committedEvent.CommandSeq <= commandSeq)
      .Where(committedEvent => committedEvent.EventType is CommittedEventType.TurnStarted)
      .Select(committedEvent => PayloadInt(committedEvent, "turnNumber"))
      .Where(turnNumber => turnNumber is not null)
      .Cast<int>()
      .DefaultIfEmpty(1)
      .Max();
    return Math.Max(1, eventTurn);
  }

  private static ReconstructedEvent ToEvent(CommittedEventRecord committedEvent) =>
    new(
      committedEvent.EventSeq,
      committedEvent.EventType,
      committedEvent.MutationBatchRef,
      committedEvent.MutationRefs,
      committedEvent.PreStateHash,
      committedEvent.PostStateHash);

  private static IReadOnlyList<ReconstructedDelta> ToDeltas(CommittedEventRecord committedEvent)
  {
    var target = committedEvent.TargetRefs.FirstOrDefault();
    var subject = target?.RefId ?? committedEvent.SourceRefs.FirstOrDefault()?.RefId ?? "game";
    var playerId = FirstPayloadString(committedEvent, "playerId");
    var metadata = committedEvent.Payload
      .ToDictionary(
        entry => entry.Key,
        entry => PayloadValueToString(entry.Value),
        StringComparer.Ordinal);

    return committedEvent.EventType switch
    {
      CommittedEventType.CardMovedZone => [Delta("ZONE_DELTA", subject, playerId, "Card moved between zones.", metadata)],
      CommittedEventType.CardDrawn => [Delta("HAND_DELTA", subject, playerId, "Card drawn into hand.", metadata)],
      CommittedEventType.MinionSummoned => [Delta("BOARD_DELTA", subject, playerId, "Minion summoned to board.", metadata)],
      CommittedEventType.EntityDamaged => [Delta("DAMAGE_DELTA", subject, playerId, "Entity damage changed.", metadata)],
      CommittedEventType.EntityHealed => [Delta("DAMAGE_DELTA", subject, playerId, "Entity damage healed.", metadata)],
      CommittedEventType.EntityDestroyed => [Delta("DEATH_DELTA", subject, playerId, "Entity destroyed and removed from board.", metadata)],
      CommittedEventType.ManaChanged => [Delta("RESOURCE_DELTA", subject, playerId, "Mana changed.", metadata)],
      CommittedEventType.GameEnded => [Delta("TERMINAL_DELTA", subject, playerId, "Game terminal state reached.", metadata)],
      _ => []
    };
  }

  private static ReconstructedDelta Delta(
    string type,
    string subject,
    string? playerId,
    string description,
    IReadOnlyDictionary<string, string> metadata) =>
    new(type, subject, playerId, description, metadata);

  private static string? FirstPayloadString(CommittedEventRecord committedEvent, string key) =>
    committedEvent.Payload
      .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal))
      ?.Value.StringValue;

  private static int? PayloadInt(CommittedEventRecord committedEvent, string key) =>
    committedEvent.Payload
      .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal))
      ?.Value.IntValue;

  private static string PayloadValueToString(EventPayloadValue value)
  {
    if (value.StringValue is not null)
    {
      return value.StringValue;
    }

    if (value.IntValue is not null)
    {
      return value.IntValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    if (value.BoolValue is not null)
    {
      return value.BoolValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    return string.Join(",", value.StringValues);
  }
}
