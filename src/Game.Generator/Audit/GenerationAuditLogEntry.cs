using Game.Generator.Validation;

namespace Game.Generator.Audit;

public sealed record GenerationAuditLogEntry(
  string GenerationId,
  DateTimeOffset CreatedAt,
  string PipelineVersion,
  string ContextVersion,
  string RulesetProfileId,
  string RulesetProfileHash,
  string? IntentRef,
  string? RawStructuredOutputRef,
  string? SkeletonRef,
  string? EffectSlotsRef,
  string? ExpandedEffectsRef,
  IReadOnlyList<GenerationValidationStageSummary> ValidationResults,
  IReadOnlyList<string> RepairAttemptRefs,
  string? BalanceReportRef,
  string? SimulationReportRef,
  string? ReplayReportRef,
  string? ImageAssetManifestRef,
  string? AnimationAssetManifestRef,
  IReadOnlyList<string> ProviderProvenanceRefs,
  string? PublicationRef,
  GeneratedCardValidationDecision FinalDecision,
  string? AudioAssetManifestRef = null);
