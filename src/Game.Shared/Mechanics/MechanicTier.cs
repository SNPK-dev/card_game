using System.Text.Json.Serialization;

namespace Game.Shared.Mechanics;

[JsonConverter(typeof(JsonStringEnumConverter<MechanicTier>))]
public enum MechanicTier
{
  [JsonStringEnumMemberName("TIER_0")]
  Tier0,

  [JsonStringEnumMemberName("TIER_1")]
  Tier1,

  [JsonStringEnumMemberName("TIER_2")]
  Tier2
}
