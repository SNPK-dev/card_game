namespace Game.Shared.Events;

public sealed record EventRef(
  string RefType,
  string RefId,
  string? Zone,
  string? ControllerPlayerId);

public sealed record EventPayloadEntry(
  string Key,
  EventPayloadValue Value);

public sealed record EventPayloadValue(
  string ValueKind,
  string? StringValue,
  int? IntValue,
  bool? BoolValue,
  IReadOnlyList<string> StringValues);
