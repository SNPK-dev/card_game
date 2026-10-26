using Game.Generator.Audit;
using Game.Generator.Providers;
using Game.Generator.Validation;
using Game.Shared.Engine;

namespace Game.Generator.Generation;

public sealed record ProductionCardGenerationRequest(
  string GenerationId,
  string PipelineVersion,
  string ContextVersion,
  string PromptVersion,
  string ModelId,
  string Prompt,
  string CardId,
  string CardVersionId,
  string CatalogSnapshotId,
  GenerationContext Context,
  DateTimeOffset CreatedAt,
  int MaxRepairAttempts,
  EngineCapabilityRegistry EngineCapabilityRegistry,
  GeneratedCardBalanceReport? BalanceReport = null,
  GeneratedCardSimulationReport? SimulationReport = null,
  GeneratedCardReplayReport? ReplayReport = null);

public sealed record ProductionCardGenerationResult(
  ProductionCardGenerationStatus Status,
  Game.Shared.Catalog.CardVersion? CardVersion,
  Game.Shared.Catalog.CatalogSnapshot? CatalogSnapshot,
  IReadOnlyList<Game.Shared.Assets.AssetManifest> AssetManifests,
  GenerationAuditLogEntry? AuditLogEntry,
  IReadOnlyList<string> RepairAttemptRefs,
  IReadOnlyList<ProductionCardGenerationDiagnostic> Diagnostics)
{
  public bool Succeeded => Status is ProductionCardGenerationStatus.Succeeded;
}
