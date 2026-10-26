using System.Text.Json.Serialization;

namespace Game.Shared.Ruleset;

[JsonConverter(typeof(JsonStringEnumConverter<FormatMechanicStateKind>))]
public enum FormatMechanicStateKind
{
  [JsonStringEnumMemberName("ENABLED")]
  Enabled,

  [JsonStringEnumMemberName("DISABLED")]
  Disabled,

  [JsonStringEnumMemberName("LEGACY_ONLY")]
  LegacyOnly,

  [JsonStringEnumMemberName("EXPERIMENTAL_ONLY")]
  ExperimentalOnly
}
