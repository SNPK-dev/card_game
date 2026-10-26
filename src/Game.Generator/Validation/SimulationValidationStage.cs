using System.Globalization;

namespace Game.Generator.Validation;

public sealed class SimulationValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Simulation;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();
    var policy = request.Context.SimulationPolicy;
    var report = request.SimulationReport;

    if (RequiresSimulationEvidence(request) && report is null)
    {
      diagnostics.Add(new GeneratedCardValidationDiagnostic(
        GeneratedCardValidationCode.SimulationReportRequired,
        GeneratedCardValidationSeverity.Error,
        "validation.simulationReport",
        string.Empty,
        $"Simulation policy '{policy.PolicyId}' requires simulation evidence."));
    }

    if (report is not null)
    {
      AddMissingValues(
        diagnostics,
        policy.RequiredScenarioSets,
        report.ScenarioSets,
        GeneratedCardValidationCode.SimulationScenarioSetMissing,
        "validation.simulationReport.scenarioSets");
      AddMissingValues(
        diagnostics,
        policy.RequiredEdgeCases,
        report.EdgeCases,
        GeneratedCardValidationCode.SimulationEdgeCaseMissing,
        "validation.simulationReport.edgeCases");
      AddIfBelowMinimum(
        diagnostics,
        report.RandomBotGames,
        policy.MinRandomBotGames,
        "validation.simulationReport.randomBotGames");
      AddIfBelowMinimum(
        diagnostics,
        report.BaselineDeckGames,
        policy.MinBaselineDeckGames,
        "validation.simulationReport.baselineDeckGames");
      AddReplayHashDiagnostic(diagnostics, policy.RequireReplayHashConsistency, report.ReplayHashConsistent);
      AddIfAboveMaximum(
        diagnostics,
        report.MaxObservedTurns,
        policy.MaxTurns,
        GeneratedCardValidationCode.SimulationTurnLimitExceeded,
        "validation.simulationReport.maxObservedTurns");
      AddIfAboveMaximum(
        diagnostics,
        report.MaxObservedEventCount,
        policy.MaxEventCount,
        GeneratedCardValidationCode.SimulationEventLimitExceeded,
        "validation.simulationReport.maxObservedEventCount");
    }

    return CreateReport(diagnostics);
  }

  private static bool RequiresSimulationEvidence(GeneratedCardValidationRequest request)
  {
    var policy = request.Context.SimulationPolicy;
    return request.Context.PublicationPolicy.RequireSimulationPass
      || policy.RequiredScenarioSets.Count > 0
      || policy.RequiredEdgeCases.Count > 0
      || policy.MinRandomBotGames > 0
      || policy.MinBaselineDeckGames > 0
      || policy.RequireReplayHashConsistency;
  }

  private static void AddMissingValues(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    IEnumerable<string> requiredValues,
    IEnumerable<string> actualValues,
    GeneratedCardValidationCode code,
    string path)
  {
    var actual = actualValues.ToHashSet(StringComparer.Ordinal);
    foreach (var missing in requiredValues.Where(value => !actual.Contains(value)).Order(StringComparer.Ordinal))
    {
      diagnostics.Add(new GeneratedCardValidationDiagnostic(
        code,
        GeneratedCardValidationSeverity.Error,
        path,
        missing,
        $"Required simulation evidence '{missing}' is missing."));
    }
  }

  private static void AddIfBelowMinimum(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    int actual,
    int minimum,
    string path)
  {
    if (actual >= minimum)
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.SimulationGameCountInsufficient,
      GeneratedCardValidationSeverity.Error,
      path,
      actual.ToString(CultureInfo.InvariantCulture),
      $"Value '{actual}' is below required minimum '{minimum}'."));
  }

  private static void AddReplayHashDiagnostic(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    bool required,
    bool actual)
  {
    if (!required || actual)
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.ReplayHashConsistencyRequired,
      GeneratedCardValidationSeverity.Error,
      "validation.simulationReport.replayHashConsistent",
      actual.ToString(CultureInfo.InvariantCulture),
      "Replay hash consistency is required by simulation policy."));
  }

  private static void AddIfAboveMaximum(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    int actual,
    int maximum,
    GeneratedCardValidationCode code,
    string path)
  {
    if (maximum <= 0 || actual <= maximum)
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      code,
      GeneratedCardValidationSeverity.Error,
      path,
      actual.ToString(CultureInfo.InvariantCulture),
      $"Value '{actual}' exceeds limit '{maximum}'."));
  }

  private static GeneratedCardValidationStageReport CreateReport(
    IReadOnlyList<GeneratedCardValidationDiagnostic> diagnostics) =>
    new(
      GeneratedCardValidationStageKind.Simulation,
      diagnostics.Any(diagnostic => diagnostic.Severity is GeneratedCardValidationSeverity.Error)
        ? GeneratedCardValidationDecision.Rejected
        : GeneratedCardValidationDecision.Accepted,
      diagnostics);
}
