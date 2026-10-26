namespace Game.Generator.Validation;

public sealed record GeneratedCardSimulationEvidenceReport(
  GeneratedCardSimulationReport SimulationReport,
  GeneratedCardReplayReport ReplayReport);
