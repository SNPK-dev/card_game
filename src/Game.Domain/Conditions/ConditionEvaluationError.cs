namespace Game.Domain.Conditions;

public sealed record ConditionEvaluationError(
  ConditionEvaluationErrorCode Code,
  string Message,
  string? ConditionKind = null);
