using Game.Generator.Acceptance;
using Game.Generator.Audit;
using Game.Generator.Generation;
using Game.Generator.Providers;
using Game.Generator.Validation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Generator.Tests;

public sealed class ProductionAcceptanceReportBuilderTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-26T00:00:00Z");
  private const string ValidReadinessHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

  [Fact]
  public void BuilderProducesBlockedReportWithAssetsReplayAndProviderEvidence()
  {
    var card = CreateCardVersion("card_alpha", "card_alpha_v1");
    var asset = CreateAsset(card);
    var match = new AcceptedMatchEvidence(
      "match-acceptance",
      "simulation-acceptance",
      "sha256:final",
      "HERO_DEFEATED",
      "player-1",
      CommandCount: 2,
      EventCount: 4,
      ReplayRuns: 2,
      ReplayVerified: true,
      ReconstructionTurnCount: 1,
      [asset.AssetManifestId],
      [
        new ExternalMatchEvidenceArtifact(
          "REPLAY_RNG_TRACE",
          "match-acceptance/rng-trace.json",
          "sha256:rng-trace")
      ]);
    var missingProvider = new GenerationProviderConfiguration(
      "real-provider",
      "model-v1",
      Endpoint: null,
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 1);
    var providerHarness = new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Blocked,
      GenerationResult: null,
      [
        new ProductionCardGenerationDiagnostic(
          "PROVIDER_CONFIGURATION_MISSING",
          "providers.structured.endpoint",
          "structured",
          "Provider configuration is missing.")
      ],
      [missingProvider.Redacted()]);
    var providerEvidenceReport = new ProviderEvidenceReportBuilder().Build(new ProviderEvidenceReportRequest(
      "provider-evidence-acceptance-001",
      FixedNow,
      providerHarness));

    var report = new ProductionAcceptanceReportBuilder().Build(new ProductionAcceptanceReportRequest(
      "acceptance-report-001",
      FixedNow,
      [card],
      [new DeckDefinition("deck-alpha", "player-1", "catalog-alpha", "ruleset_mvp_core_v1", [new DeckCardRef(card.CardId, 1)])],
      [asset],
      match,
      [CreateAudit(card)],
      providerHarness,
      ["real provider configuration is absent"],
      providerEvidenceReport));

    Assert.Equal(ProductionAcceptanceStatus.Blocked, report.Status);
    Assert.Equal("acceptance-report-001", report.ReportId);
    var cardEvidence = Assert.Single(report.GeneratedCards);
    Assert.Equal(card.CardVersionId, cardEvidence.CardVersionId);
    Assert.Equal(["asset-card_alpha_v1"], cardEvidence.AssetManifestRefs);
    Assert.Equal("generation-audit:generation-alpha", cardEvidence.AuditRef);
    Assert.Equal(match.FinalStateHash, report.Match.FinalStateHash);
    Assert.True(report.Match.ReplayVerified);
    Assert.Equal(["asset-card_alpha_v1"], report.Match.ReconstructionAssetRefs);
    Assert.Equal("match-acceptance/rng-trace.json", Assert.Single(report.Match.EvidenceArtifacts).ArtifactRef);
    Assert.Equal(ProductionCardGenerationStatus.Blocked, report.ProviderEvidence.Status);
    Assert.Contains(report.ProviderEvidence.Diagnostics, diagnostic =>
      diagnostic.StartsWith("PROVIDER_CONFIGURATION_MISSING:providers.structured.endpoint", StringComparison.Ordinal));
    Assert.DoesNotContain(report.ProviderEvidence.RedactedProviderRefs, providerRef =>
      providerRef.Contains("secret", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(providerEvidenceReport.ReportId, report.ProviderEvidence.EvidenceReportRef);
    Assert.Contains("provider-evidence-report:provider-evidence-acceptance-001", report.ProviderEvidence.RedactedProviderRefs);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Security.Status);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Recovery.Status);
    Assert.Equal(AcceptanceGateEvidenceStatus.Blocked, report.ReadinessEvidence.Load.Status);
    Assert.Equal(["real provider configuration is absent"], report.ResidualRisks);
  }

  [Fact]
  public void BuilderBlocksReportWhenReadinessEvidenceIsMissing()
  {
    var card = CreateCardVersion("card_alpha", "card_alpha_v1");
    var asset = CreateAsset(card);

    var report = new ProductionAcceptanceReportBuilder().Build(new ProductionAcceptanceReportRequest(
      "acceptance-report-missing-readiness",
      FixedNow,
      [card],
      [CreateDeck(card)],
      [asset],
      CreateMatch(asset),
      [CreateAudit(card)],
      CreateSucceededProviderHarness(),
      ResidualRisks: []));

    Assert.Equal(ProductionAcceptanceStatus.Blocked, report.Status);
    Assert.False(report.ReadinessEvidence.IsAccepted);
    Assert.Contains(report.ReadinessEvidence.Security.Diagnostics, diagnostic =>
      diagnostic.Contains("L9_SECURITY:evidence artifacts are required", StringComparison.Ordinal));
    Assert.Contains(report.ReadinessEvidence.Recovery.Diagnostics, diagnostic =>
      diagnostic.Contains("L10_RECOVERY:evidence artifacts are required", StringComparison.Ordinal));
    Assert.Contains(report.ReadinessEvidence.Load.Diagnostics, diagnostic =>
      diagnostic.Contains("L10_LOAD:evidence artifacts are required", StringComparison.Ordinal));
  }

  [Fact]
  public void BuilderAcceptsReportWhenProviderReplayAndReadinessEvidencePass()
  {
    var card = CreateCardVersion("card_alpha", "card_alpha_v1");
    var asset = CreateAsset(card);
    var readinessReport = CreatePassedReadinessReport();

    var report = new ProductionAcceptanceReportBuilder().Build(new ProductionAcceptanceReportRequest(
      "acceptance-report-ready",
      FixedNow,
      [card],
      [CreateDeck(card)],
      [asset],
      CreateMatch(asset),
      [CreateAudit(card)],
      CreateSucceededProviderHarness(),
      ResidualRisks: [],
      ReadinessEvidenceReport: readinessReport));

    Assert.Equal(ProductionAcceptanceStatus.Accepted, report.Status);
    Assert.True(report.ReadinessEvidence.IsAccepted);
    Assert.Equal(readinessReport.ReportId, report.ReadinessEvidence.EvidenceReportRef);
    Assert.Equal(["artifacts/security/e2e-009-abuse.json"], report.ReadinessEvidence.Security.EvidenceRefs);
    Assert.Equal(["artifacts/recovery/e2e-007-restart.json"], report.ReadinessEvidence.Recovery.EvidenceRefs);
    Assert.Equal(["artifacts/load/load-acceptance.json"], report.ReadinessEvidence.Load.EvidenceRefs);
  }

  [Fact]
  public void BuilderRejectsConflictingReadinessEvidenceSources()
  {
    var card = CreateCardVersion("card_alpha", "card_alpha_v1");
    var asset = CreateAsset(card);

    var exception = Assert.Throws<ArgumentException>(() =>
      new ProductionAcceptanceReportBuilder().Build(new ProductionAcceptanceReportRequest(
        "acceptance-report-conflicting-readiness",
        FixedNow,
        [card],
        [CreateDeck(card)],
        [asset],
        CreateMatch(asset),
        [CreateAudit(card)],
        CreateSucceededProviderHarness(),
        ResidualRisks: [],
        ReadinessEvidence: ProductionReadinessEvidence.Missing(),
        ReadinessEvidenceReport: CreatePassedReadinessReport())));

    Assert.Contains("both explicit readiness evidence and readiness evidence report", exception.Message);
  }

  private static CardVersion CreateCardVersion(string cardId, string cardVersionId) =>
    new(
      cardId,
      cardVersionId,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:profile",
      FixedNow,
      new CardDefinition(
        cardId,
        cardVersionId,
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        "catalog-alpha",
        new StaticCardData(
          "Alpha",
          CardType.MinionCard,
          ["mage"],
          Rarity.Common,
          1,
          [],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(new MinionDefinition(1, 1, [], [], CanAttack: true), null, null, null, null, null),
        [],
        new CardTextGeneration("Alpha.", null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata("asset-card_alpha_v1", "asset://card-alpha", null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static AssetManifest CreateAsset(CardVersion card) =>
    new(
      "asset-card_alpha_v1",
      "asset-manifest-bootstrap-0.1.0",
      card.CardId,
      card.CardVersionId,
      VisualAssetMode.StaticImage,
      AudioAssetMode.None,
      new VisualAssetManifest(
        "visual-card_alpha_v1",
        "visual-v1",
        "asset://card-alpha",
        "png",
        AssetModerationStatus.Passed,
        "sha256:visual"),
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      "sha256:asset");

  private static DeckDefinition CreateDeck(CardVersion card) =>
    new("deck-alpha", "player-1", "catalog-alpha", "ruleset_mvp_core_v1", [new DeckCardRef(card.CardId, 1)]);

  private static AcceptedMatchEvidence CreateMatch(AssetManifest asset) =>
    new(
      "match-acceptance",
      "simulation-acceptance",
      "sha256:final",
      "HERO_DEFEATED",
      "player-1",
      CommandCount: 2,
      EventCount: 4,
      ReplayRuns: 2,
      ReplayVerified: true,
      ReconstructionTurnCount: 1,
      [asset.AssetManifestId],
      [
        new ExternalMatchEvidenceArtifact(
          "REPLAY_RNG_TRACE",
          "match-acceptance/rng-trace.json",
          "sha256:rng-trace")
      ]);

  private static RealProviderGenerationHarnessResult CreateSucceededProviderHarness()
  {
    var provider = new GenerationProviderConfiguration(
      "local-provider",
      "model-v1",
      new Uri("http://127.0.0.1:11434"),
      ApiKey: null,
      TimeSpan.FromSeconds(60),
      MaxRetryAttempts: 1);

    return new RealProviderGenerationHarnessResult(
      ProductionCardGenerationStatus.Succeeded,
      GenerationResult: null,
      Diagnostics: [],
      [provider.Redacted()]);
  }

  private static ProductionReadinessEvidenceReport CreatePassedReadinessReport() =>
    new ProductionReadinessEvidenceReportBuilder().Build(new ProductionReadinessEvidenceReportRequest(
      "readiness-evidence-report-001",
      FixedNow,
      [
        new ProductionReadinessEvidenceArtifact(
          "L9_SECURITY",
          "SECURITY_ABUSE_REPORT",
          "artifacts/security/e2e-009-abuse.json",
          ValidReadinessHash,
          FixedNow),
        new ProductionReadinessEvidenceArtifact(
          "L10_RECOVERY",
          "RECOVERY_RESTART_REPORT",
          "artifacts/recovery/e2e-007-restart.json",
          ValidReadinessHash,
          FixedNow),
        new ProductionReadinessEvidenceArtifact(
          "L10_LOAD",
          "LOAD_TEST_REPORT",
          "artifacts/load/load-acceptance.json",
          ValidReadinessHash,
          FixedNow)
      ]));

  private static GenerationAuditLogEntry CreateAudit(CardVersion card) =>
    new(
      "generation-alpha",
      FixedNow,
      "pipeline-v1",
      "context-v1",
      "ruleset_mvp_core_v1",
      "sha256:profile",
      "intent:generation-alpha",
      "raw:generation-alpha",
      "skeleton:generation-alpha",
      EffectSlotsRef: null,
      ExpandedEffectsRef: null,
      ValidationResults: [],
      RepairAttemptRefs: [],
      BalanceReportRef: null,
      SimulationReportRef: "simulation-report",
      ReplayReportRef: "replay-report",
      ImageAssetManifestRef: "asset-card_alpha_v1",
      AnimationAssetManifestRef: null,
      ProviderProvenanceRefs: ["provider:model:raw"],
      PublicationRef: card.CardVersionId,
      GeneratedCardValidationDecision.Accepted);
}
