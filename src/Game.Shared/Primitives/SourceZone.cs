using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<SourceZone>))]
public enum SourceZone
{
  [JsonStringEnumMemberName("BOARD")]
  Board,

  [JsonStringEnumMemberName("HAND")]
  Hand,

  [JsonStringEnumMemberName("DECK")]
  Deck,

  [JsonStringEnumMemberName("SECRET_ZONE")]
  SecretZone,

  [JsonStringEnumMemberName("QUEST_ZONE")]
  QuestZone,

  [JsonStringEnumMemberName("GRAVEYARD")]
  Graveyard,

  [JsonStringEnumMemberName("SET_ASIDE")]
  SetAside,

  [JsonStringEnumMemberName("GLOBAL")]
  Global
}
