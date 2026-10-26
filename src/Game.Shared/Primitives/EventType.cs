using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType
{
  [JsonStringEnumMemberName("CARD_PLAYED")]
  CardPlayed,

  [JsonStringEnumMemberName("CARD_SUMMONED")]
  CardSummoned,

  [JsonStringEnumMemberName("TURN_STARTED")]
  TurnStarted,

  [JsonStringEnumMemberName("TURN_ENDED")]
  TurnEnded,

  [JsonStringEnumMemberName("DAMAGE_DEALT")]
  DamageDealt,

  [JsonStringEnumMemberName("ENTITY_DIED")]
  EntityDied,

  [JsonStringEnumMemberName("CARD_DRAWN")]
  CardDrawn
}
