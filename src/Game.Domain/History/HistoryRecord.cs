using Game.Shared.Events;

namespace Game.Domain.History;

public sealed record HistoryRecord(
  string HistoryRecordId,
  long EventSeq,
  long? CommandSeq,
  CommittedEventType EventType,
  string? PlayerId,
  IReadOnlyList<EventRef> SourceRefs,
  IReadOnlyList<EventRef> TargetRefs,
  IReadOnlyList<EventPayloadEntry> Payload);

public sealed record DeadMinionHistoryCandidate(
  string OwnerPlayerId,
  string SourceCardInstanceId,
  string SourceCardVersion,
  long EventSeq,
  string HistoryRecordId);
