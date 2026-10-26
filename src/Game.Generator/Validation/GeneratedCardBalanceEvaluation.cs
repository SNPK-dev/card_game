using Game.Shared.Dsl;
using Game.Generator.Generation;

namespace Game.Generator.Validation;

public enum GeneratedCardBalanceDecision
{
  Accepted,
  ReviewRequired,
  Rejected
}

public enum GeneratedCardBalanceRiskTier
{
  Low,
  Medium,
  High,
  Critical
}

public enum GeneratedCardBalanceSignalKind
{
  Complexity,
  Randomness,
  SimulationWinrateDelta,
  EventCountDelta,
  RiskFlag,
  ReportRequired,
  HumanReviewThreshold
}

public sealed record GeneratedCardBalanceEvaluationRequest(
  string EvaluationId,
  CardDefinition Candidate,
  GenerationContext Context,
  GeneratedCardBalanceReport? BalanceReport = null);

public sealed record GeneratedCardBalanceEvaluation(
  string EvaluationId,
  string PolicyId,
  string CardId,
  string CardVersionId,
  GeneratedCardBalanceDecision Decision,
  GeneratedCardBalanceRiskTier RiskTier,
  double RiskScore,
  IReadOnlyList<GeneratedCardBalanceSignal> Signals,
  IReadOnlyList<string> RepairHints);

public sealed record GeneratedCardBalanceSignal(
  GeneratedCardBalanceSignalKind Kind,
  string Path,
  string ActualValue,
  string Limit,
  bool BlocksPublication,
  string Message);
