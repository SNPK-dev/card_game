using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<EntityType>))]
public enum EntityType
{
  [JsonStringEnumMemberName("MINION")]
  Minion,

  [JsonStringEnumMemberName("HERO")]
  Hero,

  [JsonStringEnumMemberName("WEAPON")]
  Weapon,

  [JsonStringEnumMemberName("LOCATION")]
  Location,

  [JsonStringEnumMemberName("SECRET")]
  Secret,

  [JsonStringEnumMemberName("QUEST")]
  Quest,

  [JsonStringEnumMemberName("CARD_IN_HAND")]
  CardInHand,

  [JsonStringEnumMemberName("CARD_IN_DECK")]
  CardInDeck,

  [JsonStringEnumMemberName("CARD_IN_GRAVEYARD")]
  CardInGraveyard,

  [JsonStringEnumMemberName("GLOBAL_RULE")]
  GlobalRule
}
