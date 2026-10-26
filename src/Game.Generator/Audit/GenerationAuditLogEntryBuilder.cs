using Game.Generator.Validation;

namespace Game.Generator.Audit;

public sealed class GenerationAuditLogEntryBuilder
{
  public GenerationAuditLogEntry Build(
    GeneratedCardValidationRequest request,
    GeneratedCardValidationPipelineReport validationReport,
    GenerationAuditLogEntryMetadata metadata)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(validationReport);
    ArgumentNullException.ThrowIfNull(metadata);

    if (!string.Equals(request.GenerationId, validationReport.GenerationId, StringComparison.Ordinal))
    {
      throw new ArgumentException("Validation report generation id must match request generation id.", nameof(validationReport));
    }

    return new GenerationAuditLogEntry(
      validationReport.GenerationId,
      metadata.CreatedAt,
      validationReport.PipelineVersion,
      validationReport.ContextVersion,
      validationReport.RulesetProfileId,
      validationReport.RulesetProfileHash,
      metadata.IntentRef,
      metadata.RawStructuredOutputRef,
      metadata.SkeletonRef,
      metadata.EffectSlotsRef,
      metadata.ExpandedEffectsRef,
      validationReport.StageReports
        .Select(report => new GenerationValidationStageSummary(
          report.Stage,
          report.Decision,
          report.Diagnostics))
        .ToArray(),
      metadata.RepairAttemptRefs,
      metadata.BalanceReportRef,
      metadata.SimulationReportRef,
      metadata.ReplayReportRef,
      metadata.ImageAssetManifestRef,
      metadata.AnimationAssetManifestRef,
      metadata.ProviderProvenanceRefs ?? [],
      metadata.PublicationRef,
      validationReport.FinalDecision,
      metadata.AudioAssetManifestRef);
  }
}
