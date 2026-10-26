namespace Game.Domain.Conditions;

public enum ConditionEvaluationErrorCode
{
  UnsupportedConditionKind,
  MissingParameter,
  InvalidParameter,
  MissingTargetSpec,
  MissingSource,
  UnsupportedSourceKind,
  MissingPool,
  PoolResolutionFailed
}
