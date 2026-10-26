namespace Game.Generator.Validation;

public sealed record GeneratedCardValidationStageReport(
  GeneratedCardValidationStageKind Stage,
  GeneratedCardValidationDecision Decision,
  IReadOnlyList<GeneratedCardValidationDiagnostic> Diagnostics);
