using System.Text.Json.Serialization;
using Game.Shared.Primitives;

namespace Game.Shared.Events;

public sealed record ProposedEventRecord(
  string EventId,
  long EventSeqCandidate,
  long CommandSeq,
  long? ParentEventSeq,
  Phase Phase,
  ProposedEventType ProposedEventType,
  IReadOnlyList<EventRef> SourceRefs,
  IReadOnlyList<EventRef> TargetRefs,
  string ProposedOperationRef,
  IReadOnlyList<EventPayloadEntry> Payload,
  string ReplacementWindow,
  bool PreventionAllowed,
  IReadOnlyList<string> RngRollRefs,
  EventVisibility Visibility);

[JsonConverter(typeof(JsonStringEnumConverter<ProposedEventType>))]
public enum ProposedEventType
{
  [JsonStringEnumMemberName("DAMAGE_WOULD_BE_DEALT")]
  DamageWouldBeDealt,

  [JsonStringEnumMemberName("CARD_WOULD_BE_DRAWN")]
  CardWouldBeDrawn,

  [JsonStringEnumMemberName("ENTITY_WOULD_DIE")]
  EntityWouldDie,

  [JsonStringEnumMemberName("ENTITY_WOULD_BE_DESTROYED")]
  EntityWouldBeDestroyed,

  [JsonStringEnumMemberName("CARD_WOULD_MOVE_ZONE")]
  CardWouldMoveZone,

  [JsonStringEnumMemberName("HEAL_WOULD_BE_APPLIED")]
  HealWouldBeApplied,

  [JsonStringEnumMemberName("MODIFIER_WOULD_BE_APPLIED")]
  ModifierWouldBeApplied
}
