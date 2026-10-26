using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<EffectKind>))]
public enum EffectKind
{
  [JsonStringEnumMemberName("SIMPLE_PLAY_EFFECT")]
  SimplePlayEffect,

  [JsonStringEnumMemberName("TRIGGERED_BOARD_EFFECT")]
  TriggeredBoardEffect,

  [JsonStringEnumMemberName("DEATHRATTLE_EFFECT")]
  DeathrattleEffect,

  [JsonStringEnumMemberName("SECRET_EFFECT")]
  SecretEffect,

  [JsonStringEnumMemberName("QUEST_EFFECT")]
  QuestEffect,

  [JsonStringEnumMemberName("AURA_EFFECT")]
  AuraEffect,

  [JsonStringEnumMemberName("LOCATION_ABILITY_EFFECT")]
  LocationAbilityEffect,

  [JsonStringEnumMemberName("MODIFIER_EFFECT")]
  ModifierEffect,

  [JsonStringEnumMemberName("POOL_BASED_EFFECT")]
  PoolBasedEffect,

  [JsonStringEnumMemberName("BATCH_EFFECT")]
  BatchEffect,

  [JsonStringEnumMemberName("COMPOSITE_EFFECT")]
  CompositeEffect
}
