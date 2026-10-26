namespace Game.Generator.Audit;

public sealed record GenerationAuditLogEntryMetadata(
  DateTimeOffset CreatedAt,
  string? IntentRef,
  string? RawStructuredOutputRef,
  string? SkeletonRef,
  string? EffectSlotsRef,
  string? ExpandedEffectsRef,
  IReadOnlyList<string> RepairAttemptRefs,
  string? BalanceReportRef,
  string? SimulationReportRef,
  string? ReplayReportRef,
  string? ImageAssetManifestRef = null,
  string? AnimationAssetManifestRef = null,
  string? AudioAssetManifestRef = null,
  IReadOnlyList<string>? ProviderProvenanceRefs = null,
  string? PublicationRef = null);
