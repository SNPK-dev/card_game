using System.Text.Json.Serialization;

namespace Game.Shared.Events;

[JsonConverter(typeof(JsonStringEnumConverter<EventVisibility>))]
public enum EventVisibility
{
  [JsonStringEnumMemberName("PUBLIC")]
  Public,

  [JsonStringEnumMemberName("OWNER_ONLY")]
  OwnerOnly,

  [JsonStringEnumMemberName("CONTROLLER_ONLY")]
  ControllerOnly,

  [JsonStringEnumMemberName("HIDDEN_TO_OPPONENT")]
  HiddenToOpponent,

  [JsonStringEnumMemberName("DEBUG_ONLY")]
  DebugOnly
}
