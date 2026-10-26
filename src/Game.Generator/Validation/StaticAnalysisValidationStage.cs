namespace Game.Generator.Validation;

public sealed class StaticAnalysisValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.StaticAnalysis;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();
    AddNearLimitWarning(
      diagnostics,
      request.Candidate.BalanceMetadata.ComplexityScore,
      request.Context.Limits.MaxComplexityScore,
      "card.balanceMetadata.complexityScore");
    AddNearLimitWarning(
      diagnostics,
      request.Candidate.BalanceMetadata.RandomnessScore,
      request.Context.Limits.MaxRandomnessScore,
      "card.balanceMetadata.randomnessScore");

    return new GeneratedCardValidationStageReport(
      Stage,
      GeneratedCardValidationDecision.Accepted,
      diagnostics);
  }

  private static void AddNearLimitWarning(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    int actual,
    int maximum,
    string path)
  {
    if (maximum <= 0 || actual < Math.Ceiling(maximum * 0.8))
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.StaticAnalysisReviewRecommended,
      GeneratedCardValidationSeverity.Warning,
      path,
      actual.ToString(System.Globalization.CultureInfo.InvariantCulture),
      $"Value '{actual}' is near limit '{maximum}' and should be reviewed."));
  }
}
