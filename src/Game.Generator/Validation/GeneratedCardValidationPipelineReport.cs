namespace Game.Generator.Validation;

public sealed record GeneratedCardValidationPipelineReport(
  string GenerationId,
  string PipelineVersion,
  string ContextVersion,
  string RulesetProfileId,
  string RulesetProfileHash,
  GeneratedCardValidationDecision FinalDecision,
  IReadOnlyList<GeneratedCardValidationStageReport> StageReports)
{
  public IReadOnlyList<GeneratedCardValidationDiagnostic> Diagnostics =>
    StageReports.SelectMany(stage => stage.Diagnostics).ToArray();
}
