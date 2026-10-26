using System.Text.Json.Serialization;

namespace Game.Shared.Ruleset;

[JsonConverter(typeof(JsonStringEnumConverter<RulesetProfileStatus>))]
public enum RulesetProfileStatus
{
  [JsonStringEnumMemberName("DRAFT")]
  Draft,

  [JsonStringEnumMemberName("REVIEW")]
  Review,

  [JsonStringEnumMemberName("APPROVED")]
  Approved,

  [JsonStringEnumMemberName("ACTIVE")]
  Active,

  [JsonStringEnumMemberName("LOCKED")]
  Locked,

  [JsonStringEnumMemberName("DEPRECATED")]
  Deprecated,

  [JsonStringEnumMemberName("ARCHIVED")]
  Archived
}
