namespace Game.Generator.Validation;

public sealed class GeneratedCardSimulationEvidenceBuilder
{
  public GeneratedCardSimulationEvidenceReport Build(GeneratedCardSimulationEvidenceRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.ScenarioSets);
    ArgumentNullException.ThrowIfNull(request.EdgeCases);
    ArgumentNullException.ThrowIfNull(request.MatchEvidence);

    if (string.IsNullOrWhiteSpace(request.SimulationReportId))
    {
      throw new ArgumentException("Simulation report id is required.", nameof(request));
    }

    if (string.IsNullOrWhiteSpace(request.ReplayReportId))
    {
      throw new ArgumentException("Replay report id is required.", nameof(request));
    }

    if (request.MatchEvidence.Count == 0)
    {
      throw new ArgumentException("At least one match evidence entry is required.", nameof(request));
    }

    var evidence = request.MatchEvidence
      .OrderBy(match => match.ScenarioId, StringComparer.Ordinal)
      .ThenBy(match => match.SimulationId, StringComparer.Ordinal)
      .ToArray();
    var replayConsistent = evidence.All(match =>
      match.ReplayVerified
      && string.Equals(match.FinalStateHash, match.ReplayFinalStateHash, StringComparison.Ordinal));
    var replayEvidence = evidence
      .Select(match => new GeneratedCardSimulationReplayEvidence(
        match.ScenarioId,
        match.SimulationId,
        match.CommandLogRef,
        match.ReplayVerified,
        string.Equals(match.FinalStateHash, match.ReplayFinalStateHash, StringComparison.Ordinal),
        match.ReplayRuns,
        match.ReplayFinalStateHash,
        match.FailureDiagnostics))
      .ToArray();

    return new GeneratedCardSimulationEvidenceReport(
      new GeneratedCardSimulationReport(
        request.SimulationReportId,
        request.SimulationPolicyId,
        request.ScenarioSets.Order(StringComparer.Ordinal).ToArray(),
        request.EdgeCases.Order(StringComparer.Ordinal).ToArray(),
        request.RandomBotGames,
        request.BaselineDeckGames,
        replayConsistent,
        evidence.Max(match => match.MaxObservedTurn),
        evidence.Max(match => match.EventCount),
        evidence),
      new GeneratedCardReplayReport(
        request.ReplayReportId,
        request.PublicationPolicyId,
        replayEvidence.All(replay => replay.ReplayPassed),
        replayEvidence.All(replay => replay.FinalStateHashMatched),
        replayEvidence.Sum(replay => replay.ReplayRuns),
        replayEvidence.Last().FinalStateHash,
        request.ScenarioSets.Order(StringComparer.Ordinal).ToArray(),
        replayEvidence));
  }
}
