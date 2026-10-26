using System.Globalization;

namespace Game.Generator.Validation;

public sealed class ReplayValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Replay;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();
    var policy = request.Context.PublicationPolicy;
    var report = request.ReplayReport;

    if (policy.RequireReplayPass && report is null)
    {
      diagnostics.Add(new GeneratedCardValidationDiagnostic(
        GeneratedCardValidationCode.ReplayReportRequired,
        GeneratedCardValidationSeverity.Error,
        "validation.replayReport",
        string.Empty,
        $"Publication policy '{policy.PolicyId}' requires replay evidence."));
    }

    if (report is not null)
    {
      if (!report.ReplayPassed)
      {
        diagnostics.Add(new GeneratedCardValidationDiagnostic(
          GeneratedCardValidationCode.ReplayPassRequired,
          GeneratedCardValidationSeverity.Error,
          "validation.replayReport.replayPassed",
          report.ReplayPassed.ToString(CultureInfo.InvariantCulture),
          "Replay report must indicate a successful replay pass."));
      }

      if (!report.FinalStateHashMatched)
      {
        diagnostics.Add(new GeneratedCardValidationDiagnostic(
          GeneratedCardValidationCode.ReplayFinalStateHashMismatch,
          GeneratedCardValidationSeverity.Error,
          "validation.replayReport.finalStateHashMatched",
          report.FinalStateHashMatched.ToString(CultureInfo.InvariantCulture),
          "Replay report final state hash comparison must match."));
      }

      if (report.ReplayRuns < 2)
      {
        diagnostics.Add(new GeneratedCardValidationDiagnostic(
          GeneratedCardValidationCode.ReplayRunCountInsufficient,
          GeneratedCardValidationSeverity.Error,
          "validation.replayReport.replayRuns",
          report.ReplayRuns.ToString(CultureInfo.InvariantCulture),
          "Replay preflight requires at least two runs to compare deterministic hashes."));
      }

      if (report.ReplayPassed && string.IsNullOrWhiteSpace(report.FinalStateHash))
      {
        diagnostics.Add(new GeneratedCardValidationDiagnostic(
          GeneratedCardValidationCode.ReplayFinalStateHashRequired,
          GeneratedCardValidationSeverity.Error,
          "validation.replayReport.finalStateHash",
          string.Empty,
          "Accepted replay reports must carry the final state hash."));
      }
    }

    return new GeneratedCardValidationStageReport(
      Stage,
      diagnostics.Any(diagnostic => diagnostic.Severity is GeneratedCardValidationSeverity.Error)
        ? GeneratedCardValidationDecision.Rejected
        : GeneratedCardValidationDecision.Accepted,
      diagnostics);
  }
}
