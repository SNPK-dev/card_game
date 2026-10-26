using System.Text.Json.Serialization;

namespace Game.Shared.Events;

[JsonConverter(typeof(JsonStringEnumConverter<EventCategory>))]
public enum EventCategory
{
  [JsonStringEnumMemberName("PROPOSED")]
  Proposed,

  [JsonStringEnumMemberName("COMMITTED")]
  Committed,

  [JsonStringEnumMemberName("RESOLVER")]
  Resolver,

  [JsonStringEnumMemberName("DIAGNOSTIC")]
  Diagnostic,

  [JsonStringEnumMemberName("PRESENTATION")]
  Presentation,

  [JsonStringEnumMemberName("SYSTEM")]
  System
}
