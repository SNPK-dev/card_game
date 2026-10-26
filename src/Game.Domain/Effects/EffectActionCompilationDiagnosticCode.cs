namespace Game.Domain.Effects;

public enum EffectActionCompilationDiagnosticCode
{
  ConditionEvaluationRejected,
  UnsupportedAction,
  UnsupportedValueExpression,
  UnsupportedTarget,
  MissingTarget,
  MissingPlayer,
  MissingDeckCard
}
