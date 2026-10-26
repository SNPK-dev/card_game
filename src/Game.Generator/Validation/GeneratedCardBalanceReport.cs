namespace Game.Generator.Validation;

public sealed record GeneratedCardBalanceReport(
  string ReportId,
  string PolicyId,
  double SimulationWinrateDelta,
  int ComplexityScore,
  int RandomnessSwingScore,
  double EventCountDelta,
  IReadOnlyList<string> RiskFlags);
