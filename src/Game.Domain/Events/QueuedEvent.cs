using Game.Shared.Events;

namespace Game.Domain.Events;

public sealed record QueuedEvent(
  string EventId,
  EventCategory Category,
  string EventType,
  int? ParentEventSeq,
  int CommandSeq,
  string Phase,
  string SourceRef,
  IReadOnlyList<string> TargetRefs,
  IReadOnlyList<string> CanonicalOrderingKeys,
  int Depth,
  EventVisibility Visibility);
