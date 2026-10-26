using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<Rarity>))]
public enum Rarity
{
  [JsonStringEnumMemberName("COMMON")]
  Common,

  [JsonStringEnumMemberName("RARE")]
  Rare,

  [JsonStringEnumMemberName("EPIC")]
  Epic,

  [JsonStringEnumMemberName("LEGENDARY")]
  Legendary
}
