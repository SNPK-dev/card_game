using Game.Shared.Events;
using Game.Shared.Primitives;

namespace Game.Domain.Triggers;

public sealed record TriggerMatch(
  string MatchId,
  long SourceEventSeq,
  CommittedEventType SourceEventType,
  EventType TriggerEventType,
  string TriggerId,
  string EffectId,
  string ControllerPlayerId,
  string SourceRef,
  IReadOnlyList<string> CanonicalOrderingKeys);
