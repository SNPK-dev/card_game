using System.Text.Json.Serialization;
using Game.Shared.Primitives;

namespace Game.Shared.Events;

public sealed record CommittedEventRecord(
  long EventSeq,
  long? ParentEventSeq,
  long? CommandSeq,
  CommittedEventType EventType,
  EventCategory Category,
  Phase Phase,
  IReadOnlyList<EventRef> SourceRefs,
  IReadOnlyList<EventRef> TargetRefs,
  IReadOnlyList<EventPayloadEntry> Payload,
  string? MutationBatchRef,
  IReadOnlyList<string> MutationRefs,
  IReadOnlyList<string> RngRollRefs,
  string PreStateHash,
  string PostStateHash,
  EventVisibility Visibility);

[JsonConverter(typeof(JsonStringEnumConverter<CommittedEventType>))]
public enum CommittedEventType
{
  [JsonStringEnumMemberName("GAME_STARTED")]
  GameStarted,

  [JsonStringEnumMemberName("TURN_STARTED")]
  TurnStarted,

  [JsonStringEnumMemberName("EXTRA_TURN_SCHEDULED")]
  ExtraTurnScheduled,

  [JsonStringEnumMemberName("TURN_ENDED")]
  TurnEnded,

  [JsonStringEnumMemberName("CARD_PLAYED")]
  CardPlayed,

  [JsonStringEnumMemberName("CARD_MOVED_ZONE")]
  CardMovedZone,

  [JsonStringEnumMemberName("CARD_DRAWN")]
  CardDrawn,

  [JsonStringEnumMemberName("CARD_DISCARDED")]
  CardDiscarded,

  [JsonStringEnumMemberName("CARD_RETURNED_TO_HAND")]
  CardReturnedToHand,

  [JsonStringEnumMemberName("MINION_SUMMONED")]
  MinionSummoned,

  [JsonStringEnumMemberName("ENTITY_DAMAGED")]
  EntityDamaged,

  [JsonStringEnumMemberName("DAMAGE_WOULD_BE_DEALT")]
  DamageWouldBeDealt,

  [JsonStringEnumMemberName("REPLACEMENT_RESOLVED")]
  ReplacementResolved,

  [JsonStringEnumMemberName("ENTITY_HEALED")]
  EntityHealed,

  [JsonStringEnumMemberName("MANA_CHANGED")]
  ManaChanged,

  [JsonStringEnumMemberName("ENTITY_DESTROYED")]
  EntityDestroyed,

  [JsonStringEnumMemberName("ENTITY_DIED")]
  EntityDied,

  [JsonStringEnumMemberName("DEATHRATTLE_TRIGGERED")]
  DeathrattleTriggered,

  [JsonStringEnumMemberName("SECRET_REVEALED")]
  SecretRevealed,

  [JsonStringEnumMemberName("QUEST_PROGRESS_CHANGED")]
  QuestProgressChanged,

  [JsonStringEnumMemberName("MODIFIER_APPLIED")]
  ModifierApplied,

  [JsonStringEnumMemberName("MODIFIER_EXPIRED")]
  ModifierExpired,

  [JsonStringEnumMemberName("AURA_REFRESHED")]
  AuraRefreshed,

  [JsonStringEnumMemberName("WEAPON_EQUIPPED")]
  WeaponEquipped,

  [JsonStringEnumMemberName("LOCATION_PLAYED")]
  LocationPlayed,

  [JsonStringEnumMemberName("LOCATION_ACTIVATED")]
  LocationActivated,

  [JsonStringEnumMemberName("HERO_CARD_PLAYED")]
  HeroCardPlayed,

  [JsonStringEnumMemberName("HERO_POWER_USED")]
  HeroPowerUsed,

  [JsonStringEnumMemberName("HERO_POWER_REPLACED")]
  HeroPowerReplaced,

  [JsonStringEnumMemberName("CHOICE_CREATED")]
  ChoiceCreated,

  [JsonStringEnumMemberName("CHOICE_RESOLVED")]
  ChoiceResolved,

  [JsonStringEnumMemberName("RNG_ROLLED")]
  RngRolled,

  [JsonStringEnumMemberName("GAME_ENDED")]
  GameEnded,

  [JsonStringEnumMemberName("ENGINE_FAULTED")]
  EngineFaulted
}
