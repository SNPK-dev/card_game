using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<Phase>))]
public enum Phase
{
  [JsonStringEnumMemberName("ON_PLAY")]
  OnPlay,

  [JsonStringEnumMemberName("BATTLECRY_PHASE")]
  BattlecryPhase,

  [JsonStringEnumMemberName("ON_SUMMON")]
  OnSummon,

  [JsonStringEnumMemberName("AFTER_PLAY")]
  AfterPlay,

  [JsonStringEnumMemberName("SECRET_WINDOW")]
  SecretWindow,

  [JsonStringEnumMemberName("DEATHRATTLE_PHASE")]
  DeathrattlePhase,

  [JsonStringEnumMemberName("TURN_START")]
  TurnStart,

  [JsonStringEnumMemberName("TURN_END")]
  TurnEnd,

  [JsonStringEnumMemberName("AURA_REFRESH")]
  AuraRefresh
}
