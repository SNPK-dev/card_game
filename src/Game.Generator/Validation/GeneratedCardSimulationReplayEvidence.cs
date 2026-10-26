namespace Game.Generator.Validation;

public sealed record GeneratedCardSimulationReplayEvidence(
  string ScenarioId,
  string SimulationId,
  string CommandLogRef,
  bool ReplayPassed,
  bool FinalStateHashMatched,
  int ReplayRuns,
  string? FinalStateHash,
  IReadOnlyList<string> Diagnostics);
