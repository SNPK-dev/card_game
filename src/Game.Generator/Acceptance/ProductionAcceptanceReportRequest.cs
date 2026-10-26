using Game.Generator.Audit;
using Game.Generator.Generation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;

namespace Game.Generator.Acceptance;

public sealed record ProductionAcceptanceReportRequest(
  string ReportId,
  DateTimeOffset CreatedAt,
  IReadOnlyList<CardVersion> GeneratedCards,
  IReadOnlyList<DeckDefinition> Decks,
  IReadOnlyList<AssetManifest> AssetManifests,
  AcceptedMatchEvidence Match,
  IReadOnlyList<GenerationAuditLogEntry> AuditEntries,
  RealProviderGenerationHarnessResult ProviderHarnessResult,
  IReadOnlyList<string> ResidualRisks,
  ProviderEvidenceReport? ProviderEvidenceReport = null,
  ProductionReadinessEvidence? ReadinessEvidence = null,
  ProductionReadinessEvidenceReport? ReadinessEvidenceReport = null);
