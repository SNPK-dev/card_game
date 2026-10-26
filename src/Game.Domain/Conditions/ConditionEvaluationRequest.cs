using Game.Domain.Targeting;
using Game.Domain.Pooling;
using Game.Shared.Dsl;

namespace Game.Domain.Conditions;

public sealed record ConditionEvaluationRequest(
  IReadOnlyList<Condition> Conditions,
  string ActingPlayerId,
  TargetSpec? TargetSpec = null,
  string? SourceControllerPlayerId = null,
  ResolvedTargetRef? Source = null,
  IReadOnlyList<ResolvedTargetRef>? ChosenTargets = null,
  IReadOnlyDictionary<string, FixedPoolDefinition>? Pools = null);
