namespace Game.Generator.Validation;

public sealed record GeneratedCardSimulationReport(
  string ReportId,
  string PolicyId,
  IReadOnlyList<string> ScenarioSets,
  IReadOnlyList<string> EdgeCases,
  int RandomBotGames,
  int BaselineDeckGames,
  bool ReplayHashConsistent,
  int MaxObservedTurns,
  int MaxObservedEventCount,
  IReadOnlyList<GeneratedCardSimulationMatchEvidence>? MatchEvidence = null);
