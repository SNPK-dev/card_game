using System.Text.Json.Serialization;
using Game.Shared.Primitives;

namespace Game.Shared.Mutation;

[JsonConverter(typeof(JsonStringEnumConverter<MutationCategory>))]
public enum MutationCategory
{
  [JsonStringEnumMemberName("ZONE_MUTATION")]
  ZoneMutation,

  [JsonStringEnumMemberName("STAT_MUTATION")]
  StatMutation,

  [JsonStringEnumMemberName("RESOURCE_MUTATION")]
  ResourceMutation,

  [JsonStringEnumMemberName("CARD_MUTATION")]
  CardMutation,

  [JsonStringEnumMemberName("ENTITY_MUTATION")]
  EntityMutation,

  [JsonStringEnumMemberName("MODIFIER_MUTATION")]
  ModifierMutation,

  [JsonStringEnumMemberName("AURA_MUTATION")]
  AuraMutation,

  [JsonStringEnumMemberName("HISTORY_MUTATION")]
  HistoryMutation,

  [JsonStringEnumMemberName("CHOICE_MUTATION")]
  ChoiceMutation,

  [JsonStringEnumMemberName("GAME_MUTATION")]
  GameMutation,

  [JsonStringEnumMemberName("PRESENTATION_ONLY")]
  PresentationOnly
}

[JsonConverter(typeof(JsonStringEnumConverter<MutationType>))]
public enum MutationType
{
  [JsonStringEnumMemberName("MOVE_CARD_ZONE")]
  MoveCardZone,

  [JsonStringEnumMemberName("MOVE_ENTITY_ZONE")]
  MoveEntityZone,

  [JsonStringEnumMemberName("REMOVE_FROM_ZONE")]
  RemoveFromZone,

  [JsonStringEnumMemberName("ADD_TO_ZONE")]
  AddToZone,

  [JsonStringEnumMemberName("SET_HEALTH")]
  SetHealth,

  [JsonStringEnumMemberName("MODIFY_HEALTH")]
  ModifyHealth,

  [JsonStringEnumMemberName("SET_ATTACK")]
  SetAttack,

  [JsonStringEnumMemberName("MODIFY_ATTACK")]
  ModifyAttack,

  [JsonStringEnumMemberName("APPLY_DAMAGE")]
  ApplyDamage,

  [JsonStringEnumMemberName("HEAL_ENTITY")]
  HealEntity,

  [JsonStringEnumMemberName("CLEAR_DAMAGE")]
  ClearDamage,

  [JsonStringEnumMemberName("MODIFY_MANA")]
  ModifyMana,

  [JsonStringEnumMemberName("SET_MANA")]
  SetMana,

  [JsonStringEnumMemberName("MODIFY_ARMOR")]
  ModifyArmor,

  [JsonStringEnumMemberName("MODIFY_FATIGUE")]
  ModifyFatigue,

  [JsonStringEnumMemberName("DRAW_CARD")]
  DrawCard,

  [JsonStringEnumMemberName("DISCARD_CARD")]
  DiscardCard,

  [JsonStringEnumMemberName("CREATE_CARD_INSTANCE")]
  CreateCardInstance,

  [JsonStringEnumMemberName("DESTROY_CARD_INSTANCE")]
  DestroyCardInstance,

  [JsonStringEnumMemberName("CREATE_ENTITY")]
  CreateEntity,

  [JsonStringEnumMemberName("DESTROY_ENTITY")]
  DestroyEntity,

  [JsonStringEnumMemberName("SUMMON_ENTITY")]
  SummonEntity,

  [JsonStringEnumMemberName("TRANSFORM_ENTITY")]
  TransformEntity,

  [JsonStringEnumMemberName("APPLY_MODIFIER")]
  ApplyModifier,

  [JsonStringEnumMemberName("ADD_KEYWORD")]
  AddKeyword,

  [JsonStringEnumMemberName("REMOVE_KEYWORD")]
  RemoveKeyword,

  [JsonStringEnumMemberName("EXPIRE_MODIFIER")]
  ExpireModifier,

  [JsonStringEnumMemberName("REMOVE_MODIFIER")]
  RemoveModifier,

  [JsonStringEnumMemberName("APPLY_AURA")]
  ApplyAura,

  [JsonStringEnumMemberName("REMOVE_AURA")]
  RemoveAura,

  [JsonStringEnumMemberName("REFRESH_AURA")]
  RefreshAura,

  [JsonStringEnumMemberName("APPEND_HISTORY_RECORD")]
  AppendHistoryRecord,

  [JsonStringEnumMemberName("CREATE_CHOICE")]
  CreateChoice,

  [JsonStringEnumMemberName("RESOLVE_CHOICE")]
  ResolveChoice,

  [JsonStringEnumMemberName("CLEAR_CHOICE")]
  ClearChoice,

  [JsonStringEnumMemberName("SET_PHASE")]
  SetPhase,

  [JsonStringEnumMemberName("SET_ACTIVE_PLAYER")]
  SetActivePlayer,

  [JsonStringEnumMemberName("ADVANCE_TURN")]
  AdvanceTurn,

  [JsonStringEnumMemberName("SCHEDULE_EXTRA_TURN")]
  ScheduleExtraTurn,

  [JsonStringEnumMemberName("SET_TERMINAL_STATE")]
  SetTerminalState,

  [JsonStringEnumMemberName("PLAY_AUDIO")]
  PlayAudio,

  [JsonStringEnumMemberName("STOP_AUDIO")]
  StopAudio,

  [JsonStringEnumMemberName("LOAD_AUDIO_ASSET")]
  LoadAudioAsset,

  [JsonStringEnumMemberName("SET_CLIENT_VOLUME")]
  SetClientVolume,

  [JsonStringEnumMemberName("SET_CARD_HOVER_STATE")]
  SetCardHoverState,

  [JsonStringEnumMemberName("SET_ANIMATION_FRAME")]
  SetAnimationFrame,

  [JsonStringEnumMemberName("SET_GIF_PLAYBACK_STATE")]
  SetGifPlaybackState
}

public sealed record StateMutationRecord(
  string MutationId,
  MutationType MutationType,
  MutationCategory Category,
  string SourceRef,
  string? TargetRef,
  IReadOnlyList<MutationPayloadEntry> Payload,
  Phase Phase,
  int CausedByCommandSeq,
  int? CausedByEventSeq,
  string OrderingKey,
  IReadOnlyList<MutationMetadataEntry> Metadata);

public sealed record ProposedMutationBatchRecord(
  string BatchId,
  int CommandSeq,
  int? ParentEventSeq,
  Phase Phase,
  string SourceRef,
  string Reason,
  IReadOnlyList<StateMutationRecord> Mutations,
  string PreStateHash,
  string? ReplacementPolicyRef,
  string? FizzlePolicyRef);

public sealed record AppliedMutationBatchRecord(
  string BatchId,
  int CommandSeq,
  int? ParentEventSeq,
  Phase Phase,
  string SourceRef,
  string Reason,
  IReadOnlyList<StateMutationRecord> AppliedMutations,
  IReadOnlyList<StateMutationRecord> SkippedMutations,
  string PreStateHash,
  string PostStateHash,
  IReadOnlyList<string> DerivedEventRefs,
  int AppliedAtStep);

public sealed record MutationPayloadEntry(
  string Key,
  MutationPayloadValue Value);

public sealed record MutationPayloadValue(
  string ValueKind,
  string? StringValue,
  int? IntValue,
  bool? BoolValue,
  IReadOnlyList<string> StringValues);

public sealed record MutationMetadataEntry(
  string Key,
  string Value);
