using Game.Generator.Validation;

namespace Game.Generator.Audit;

public sealed record GenerationValidationStageSummary(
  GeneratedCardValidationStageKind Stage,
  GeneratedCardValidationDecision Decision,
  IReadOnlyList<GeneratedCardValidationDiagnostic> Diagnostics);
