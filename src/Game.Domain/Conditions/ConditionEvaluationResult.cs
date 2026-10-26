namespace Game.Domain.Conditions;

public sealed record ConditionEvaluationResult(
  bool IsAccepted,
  bool AreAllSatisfied,
  IReadOnlyList<ConditionEvaluation> Evaluations,
  IReadOnlyList<ConditionEvaluationError> Errors)
{
  public static ConditionEvaluationResult Accepted(
    IReadOnlyList<ConditionEvaluation> evaluations)
  {
    return new ConditionEvaluationResult(
      true,
      evaluations.All(evaluation => evaluation.IsSatisfied),
      evaluations,
      []);
  }

  public static ConditionEvaluationResult Rejected(
    IReadOnlyList<ConditionEvaluation> evaluations,
    params ConditionEvaluationError[] errors)
  {
    return new ConditionEvaluationResult(false, false, evaluations, errors);
  }
}
