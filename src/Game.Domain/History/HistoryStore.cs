using Game.Shared.Events;

namespace Game.Domain.History;

public sealed class HistoryStore
{
  private readonly IReadOnlyList<HistoryRecord> _records;

  public HistoryStore(IReadOnlyList<HistoryRecord>? records = null)
  {
    _records = (records ?? [])
      .OrderBy(record => record.EventSeq)
      .ThenBy(record => record.HistoryRecordId, StringComparer.Ordinal)
      .ToArray();
  }

  public IReadOnlyList<HistoryRecord> Records => _records;

  public HistoryStore AppendCommittedEvents(IEnumerable<CommittedEventRecord> events)
  {
    ArgumentNullException.ThrowIfNull(events);

    var nextRecords = _records
      .Concat(events
        .Where(IsGameplayCommittedEvent)
        .OrderBy(@event => @event.EventSeq)
        .Select(CreateRecord))
      .OrderBy(record => record.EventSeq)
      .ThenBy(record => record.HistoryRecordId, StringComparer.Ordinal)
      .ToArray();

    return new HistoryStore(nextRecords);
  }

  public int CountByType(CommittedEventType eventType) =>
    _records.Count(record => record.EventType == eventType);

  public int CountByTypeAndPlayer(CommittedEventType eventType, string playerId) =>
    _records.Count(record =>
      record.EventType == eventType &&
      StringComparer.Ordinal.Equals(record.PlayerId, playerId));

  public IReadOnlyList<HistoryRecord> FindByType(CommittedEventType eventType) =>
    _records
      .Where(record => record.EventType == eventType)
      .OrderBy(record => record.EventSeq)
      .ThenBy(record => record.HistoryRecordId, StringComparer.Ordinal)
      .ToArray();

  public IReadOnlyList<DeadMinionHistoryCandidate> FindDeadMinions(
    string? ownerPlayerId = null,
    int maxCandidates = 30)
  {
    if (maxCandidates <= 0)
    {
      return [];
    }

    return _records
      .Where(record => record.EventType == CommittedEventType.EntityDied)
      .Where(record => ownerPlayerId is null || StringComparer.Ordinal.Equals(record.PlayerId, ownerPlayerId))
      .Select(CreateDeadMinionCandidate)
      .Where(candidate => candidate is not null)
      .Select(candidate => candidate!)
      .Take(maxCandidates)
      .ToArray();
  }

  private static bool IsGameplayCommittedEvent(CommittedEventRecord @event) =>
    @event.Category == EventCategory.Committed;

  private static HistoryRecord CreateRecord(CommittedEventRecord @event) =>
    new(
      $"history:{@event.EventSeq:0000000000}:{@event.EventType}",
      @event.EventSeq,
      @event.CommandSeq,
      @event.EventType,
      ResolvePlayerId(@event),
      @event.SourceRefs
        .OrderBy(EventRefOrderingKey, StringComparer.Ordinal)
        .ToArray(),
      @event.TargetRefs
        .OrderBy(EventRefOrderingKey, StringComparer.Ordinal)
        .ToArray(),
      @event.Payload
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .ToArray());

  private static string? ResolvePlayerId(CommittedEventRecord @event)
  {
    var payloadPlayerId = @event.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, "playerId"))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    if (!string.IsNullOrWhiteSpace(payloadPlayerId))
    {
      return payloadPlayerId;
    }

    return @event.SourceRefs
      .Concat(@event.TargetRefs)
      .Where(eventRef => !string.IsNullOrWhiteSpace(eventRef.ControllerPlayerId))
      .OrderBy(EventRefOrderingKey, StringComparer.Ordinal)
      .Select(eventRef => eventRef.ControllerPlayerId)
      .FirstOrDefault();
  }

  private static string EventRefOrderingKey(EventRef eventRef) =>
    $"{eventRef.ControllerPlayerId ?? string.Empty}:{eventRef.RefType}:{eventRef.RefId}:{eventRef.Zone ?? string.Empty}";

  private static DeadMinionHistoryCandidate? CreateDeadMinionCandidate(HistoryRecord record)
  {
    var sourceCardInstanceId = PayloadString(record, "sourceCardInstanceId");
    if (string.IsNullOrWhiteSpace(record.PlayerId)
      || string.IsNullOrWhiteSpace(sourceCardInstanceId))
    {
      return null;
    }

    return new DeadMinionHistoryCandidate(
      record.PlayerId,
      sourceCardInstanceId,
      PayloadString(record, "cardVersion") ?? string.Empty,
      record.EventSeq,
      record.HistoryRecordId);
  }

  private static string? PayloadString(HistoryRecord record, string key) =>
    record.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
