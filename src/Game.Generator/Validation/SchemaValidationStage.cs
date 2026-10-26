namespace Game.Generator.Validation;

public sealed class SchemaValidationStage : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Schema;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<GeneratedCardValidationDiagnostic>();
    var candidate = request.Candidate;

    AddMissing(diagnostics, candidate.SchemaVersion, "card.schemaVersion");
    AddMissing(diagnostics, candidate.EngineVersion, "card.engineVersion");
    AddMissing(diagnostics, candidate.PrimitiveRegistryVersion, "card.primitiveRegistryVersion");
    AddMissing(diagnostics, candidate.RulesetProfileId, "card.rulesetProfileId");
    AddMissing(diagnostics, candidate.StaticData.Name, "card.staticData.name");

    if (!string.Equals(candidate.RulesetProfileId, request.Context.RulesetProfileId, StringComparison.Ordinal))
    {
      diagnostics.Add(new GeneratedCardValidationDiagnostic(
        GeneratedCardValidationCode.RulesetProfileMismatch,
        GeneratedCardValidationSeverity.Error,
        "card.rulesetProfileId",
        candidate.RulesetProfileId,
        $"Candidate targets profile '{candidate.RulesetProfileId}' but context is '{request.Context.RulesetProfileId}'."));
    }

    return CreateReport(Stage, diagnostics);
  }

  private static void AddMissing(
    ICollection<GeneratedCardValidationDiagnostic> diagnostics,
    string? value,
    string path)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new GeneratedCardValidationDiagnostic(
      GeneratedCardValidationCode.RequiredFieldMissing,
      GeneratedCardValidationSeverity.Error,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }

  private static GeneratedCardValidationStageReport CreateReport(
    GeneratedCardValidationStageKind stage,
    IReadOnlyList<GeneratedCardValidationDiagnostic> diagnostics) =>
    new(
      stage,
      diagnostics.Any(diagnostic => diagnostic.Severity is GeneratedCardValidationSeverity.Error)
        ? GeneratedCardValidationDecision.Rejected
        : GeneratedCardValidationDecision.Accepted,
      diagnostics);
}
