using System.Text.Json;
using Game.Shared.Primitives;

namespace Game.Shared.Dsl;

public sealed record EffectDefinition(
  string EffectId,
  EffectKind EffectKind,
  IReadOnlyList<SourceZone> SourceZones,
  TriggerDefinition? Trigger,
  TimingPolicy TimingPolicy,
  IReadOnlyList<Condition> Conditions,
  TargetSpec? TargetRequirement,
  IReadOnlyList<ActionDefinition> Actions,
  ResolutionPolicy ResolutionPolicy,
  FizzlePolicy FizzlePolicy,
  ReplayPolicy ReplayPolicy,
  IReadOnlyList<string> Tags);

public sealed record TriggerDefinition(
  EventType EventType,
  JsonElement? EventFilter,
  string ControllerScope,
  bool OncePerEvent);

public sealed record TimingPolicy(
  Phase Phase,
  string? Window,
  int Priority,
  string OrderingPolicy);

public sealed record Condition(
  string Kind,
  JsonElement Parameters);

public sealed record TargetSpec(
  bool Required,
  TargetSelectionMode SelectionMode,
  EntityQuery Candidates,
  CountSpec Count,
  string RetargetPolicy);

public sealed record EntityQuery(
  string Controller,
  IReadOnlyList<SourceZone> Zones,
  IReadOnlyList<EntityType> EntityTypes,
  IReadOnlyList<string> RequiredTags,
  IReadOnlyList<string> ExcludedTags,
  bool AliveOnly);

public sealed record CountSpec(int Min, int Max);

public sealed record ResolutionPolicy(string Kind);

public sealed record FizzlePolicy(string Kind);

public sealed record ReplayPolicy(bool LogActionResults, bool RequiresRngLog);
