namespace Game.Domain.Conditions;

public sealed record ConditionEvaluation(
  string ConditionKind,
  bool IsSatisfied);
