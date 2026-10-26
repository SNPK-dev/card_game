namespace Game.Generator.Validation;

public sealed record GeneratedCardSimulationEvidenceRequest(
  string SimulationReportId,
  string ReplayReportId,
  string SimulationPolicyId,
  string PublicationPolicyId,
  IReadOnlyList<string> ScenarioSets,
  IReadOnlyList<string> EdgeCases,
  int RandomBotGames,
  int BaselineDeckGames,
  IReadOnlyList<GeneratedCardSimulationMatchEvidence> MatchEvidence);
