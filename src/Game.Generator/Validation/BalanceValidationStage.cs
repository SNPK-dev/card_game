namespace Game.Generator.Validation;

public sealed class BalanceValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Balance;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var evaluation = new GeneratedCardBalanceEvaluator().Evaluate(new GeneratedCardBalanceEvaluationRequest(
      $"balance:{request.GenerationId}",
      request.Candidate,
      request.Context,
      request.BalanceReport));
    var diagnostics = evaluation.Signals
      .Where(signal => signal.BlocksPublication)
      .Select(ToDiagnostic)
      .ToArray();

    return CreateReport(diagnostics);
  }

  private static GeneratedCardValidationDiagnostic ToDiagnostic(GeneratedCardBalanceSignal signal) =>
    new(
      ToValidationCode(signal.Kind),
      GeneratedCardValidationSeverity.Error,
      signal.Path,
      signal.ActualValue,
      signal.Message);

  private static GeneratedCardValidationCode ToValidationCode(GeneratedCardBalanceSignalKind kind) =>
    kind switch
    {
      GeneratedCardBalanceSignalKind.ReportRequired => GeneratedCardValidationCode.BalanceReportRequired,
      GeneratedCardBalanceSignalKind.RiskFlag => GeneratedCardValidationCode.RiskFlagAutoRejected,
      GeneratedCardBalanceSignalKind.SimulationWinrateDelta => GeneratedCardValidationCode.WinrateDeltaLimitExceeded,
      GeneratedCardBalanceSignalKind.EventCountDelta => GeneratedCardValidationCode.EventCountDeltaLimitExceeded,
      GeneratedCardBalanceSignalKind.Randomness => GeneratedCardValidationCode.RandomnessLimitExceeded,
      _ => GeneratedCardValidationCode.ComplexityLimitExceeded
    };

  private static GeneratedCardValidationStageReport CreateReport(
    IReadOnlyList<GeneratedCardValidationDiagnostic> diagnostics) =>
    new(
      GeneratedCardValidationStageKind.Balance,
      diagnostics.Any(diagnostic => diagnostic.Severity is GeneratedCardValidationSeverity.Error)
        ? GeneratedCardValidationDecision.Rejected
        : GeneratedCardValidationDecision.Accepted,
      diagnostics);
}
