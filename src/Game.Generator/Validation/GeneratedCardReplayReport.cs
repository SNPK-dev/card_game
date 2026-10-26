namespace Game.Generator.Validation;

public sealed record GeneratedCardReplayReport(
  string ReportId,
  string PolicyId,
  bool ReplayPassed,
  bool FinalStateHashMatched,
  int ReplayRuns,
  string? FinalStateHash,
  IReadOnlyList<string> CheckedScenarioSets,
  IReadOnlyList<GeneratedCardSimulationReplayEvidence>? ReplayEvidence = null);
