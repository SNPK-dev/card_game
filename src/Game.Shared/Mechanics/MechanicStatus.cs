using System.Text.Json.Serialization;

namespace Game.Shared.Mechanics;

[JsonConverter(typeof(JsonStringEnumConverter<MechanicStatus>))]
public enum MechanicStatus
{
  [JsonStringEnumMemberName("DISCOVERED")]
  Discovered,

  [JsonStringEnumMemberName("ARCHITECTURE_SUPPORTED")]
  ArchitectureSupported,

  [JsonStringEnumMemberName("SPEC_DRAFTED")]
  SpecDrafted,

  [JsonStringEnumMemberName("SPEC_APPROVED")]
  SpecApproved,

  [JsonStringEnumMemberName("IMPLEMENTED")]
  Implemented,

  [JsonStringEnumMemberName("VALIDATED")]
  Validated,

  [JsonStringEnumMemberName("SIMULATION_VERIFIED")]
  SimulationVerified,

  [JsonStringEnumMemberName("REPLAY_VERIFIED")]
  ReplayVerified,

  [JsonStringEnumMemberName("BALANCE_REVIEWED")]
  BalanceReviewed,

  [JsonStringEnumMemberName("APPROVED_FOR_RULESET")]
  ApprovedForRuleset,

  [JsonStringEnumMemberName("DEPRECATED")]
  Deprecated,

  [JsonStringEnumMemberName("REJECTED")]
  Rejected
}
