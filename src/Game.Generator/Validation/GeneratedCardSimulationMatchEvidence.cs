namespace Game.Generator.Validation;

public sealed record GeneratedCardSimulationMatchEvidence(
  string ScenarioId,
  string SimulationId,
  string SeedRef,
  string CommandLogRef,
  string InitialStateHash,
  string SetupStateHash,
  string FinalStateHash,
  int CommandCount,
  int EventCount,
  int MaxObservedTurn,
  string TerminalReason,
  string? WinningPlayerId,
  bool ReplayVerified,
  string? ReplayFinalStateHash,
  int ReplayRuns,
  IReadOnlyList<string> FailureDiagnostics);
