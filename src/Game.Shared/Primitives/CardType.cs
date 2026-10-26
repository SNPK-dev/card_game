using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<CardType>))]
public enum CardType
{
  [JsonStringEnumMemberName("MINION_CARD")]
  MinionCard,

  [JsonStringEnumMemberName("SPELL_CARD")]
  SpellCard,

  [JsonStringEnumMemberName("WEAPON_CARD")]
  WeaponCard,

  [JsonStringEnumMemberName("LOCATION_CARD")]
  LocationCard,

  [JsonStringEnumMemberName("HERO_CARD")]
  HeroCard,

  [JsonStringEnumMemberName("HERO_POWER_CARD")]
  HeroPowerCard,

  [JsonStringEnumMemberName("TOKEN_CARD")]
  TokenCard
}
