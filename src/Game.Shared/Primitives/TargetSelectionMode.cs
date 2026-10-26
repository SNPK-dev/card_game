using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<TargetSelectionMode>))]
public enum TargetSelectionMode
{
  [JsonStringEnumMemberName("PLAYER_CHOSEN")]
  PlayerChosen,

  [JsonStringEnumMemberName("RANDOM")]
  Random,

  [JsonStringEnumMemberName("AUTO_VALID")]
  AutoValid,

  [JsonStringEnumMemberName("EVENT_SOURCE")]
  EventSource,

  [JsonStringEnumMemberName("EVENT_TARGET")]
  EventTarget,

  [JsonStringEnumMemberName("PREVIOUS_ACTION_RESULT")]
  PreviousActionResult,

  [JsonStringEnumMemberName("ALL_MATCHING")]
  AllMatching,

  [JsonStringEnumMemberName("NONE")]
  None
}
