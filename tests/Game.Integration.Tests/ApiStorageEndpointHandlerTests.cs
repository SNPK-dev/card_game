using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Presentation;
using Game.Application.Rulesets;
using Game.Generator.Acceptance;
using Game.Generator.Generation;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Game.Integration.Tests;

public sealed class ApiStorageEndpointHandlerTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-29T00:00:00Z");
  private const string ValidMatchEvidenceHash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

  [Fact]
  public void StoreEndpointsPersistProductionArtifactsAndRejectDuplicates()
  {
    var catalog = CreateCatalog("catalog-api");
    var deck = new DeckDefinition(
      "deck-api",
      "player-1",
      catalog.SnapshotId,
      "ruleset_mvp_core_v1",
      [new DeckCardRef("card_arcane_apprentice", 2)]);
    var profile = CreateProfile("resolved-api", "sha256:resolved-api");
    var asset = CreateAssetManifest("asset-api", catalog.Cards.Single());
    var report = CreateAcceptanceReport("acceptance-api", deck, asset);

    var catalogStore = new InMemoryCatalogSnapshotStore();
    var deckStore = new InMemoryDeckStore();
    var profileStore = new InMemoryResolvedRulesetProfileStore();
    var assetStore = new InMemoryAssetManifestStore();
    var reportStore = new InMemoryProductionAcceptanceReportStore();
    var matchEvidenceStore = new InMemoryMatchEvidenceArtifactStore();
    var matchArtifact = new MatchEvidenceArtifact(
      "match-api/rng-trace.json",
      "match-api",
      "REPLAY_RNG_TRACE",
      ValidMatchEvidenceHash,
      FixedNow);

    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreCatalogSnapshot(catalog, catalogStore)));
    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreDeck(deck, deckStore)));
    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreResolvedRulesetProfile(profile, profileStore)));
    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreAssetManifest(asset, assetStore)));
    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreProductionAcceptanceReport(report, reportStore)));
    Assert.Equal(StatusCodes.Status200OK, StatusCode(StorageEndpointHandlers.StoreMatchEvidenceArtifact(matchArtifact, matchEvidenceStore)));

    Assert.Equal(catalog.SnapshotId, catalogStore.Latest?.SnapshotId);
    Assert.Equal(deck.DeckId, deckStore.Latest?.DeckId);
    Assert.Equal(profile.Id, profileStore.Latest?.Id);
    Assert.Equal(asset.AssetManifestId, assetStore.Latest?.AssetManifestId);
    Assert.Equal(report.ReportId, reportStore.Latest?.ReportId);
    Assert.True(reportStore.Latest?.ReadinessEvidence.IsAccepted);
    Assert.Equal("readiness-evidence-api", reportStore.Latest?.ReadinessEvidence.EvidenceReportRef);
    Assert.Equal(
      ["artifacts/security/e2e-009-abuse.json"],
      reportStore.Latest?.ReadinessEvidence.Security.EvidenceRefs);
    Assert.Equal(
      StatusCodes.Status200OK,
      StatusCode(StorageEndpointHandlers.GetProductionAcceptanceReportMatchEvidence(report.ReportId, reportStore)));
    Assert.Equal(
      StatusCodes.Status404NotFound,
      StatusCode(StorageEndpointHandlers.GetProductionAcceptanceReportMatchEvidence("missing-report", reportStore)));
    Assert.Equal(
      StatusCodes.Status200OK,
      StatusCode(StorageEndpointHandlers.GetMatchEvidenceArtifact(matchArtifact.ArtifactRef, matchEvidenceStore)));
    Assert.Equal(
      StatusCodes.Status200OK,
      StatusCode(StorageEndpointHandlers.GetMatchEvidenceArtifacts(matchArtifact.MatchId, matchEvidenceStore)));
    Assert.Equal(
      StatusCodes.Status404NotFound,
      StatusCode(StorageEndpointHandlers.GetMatchEvidenceArtifact("missing-artifact", matchEvidenceStore)));
    Assert.Equal(
      StatusCodes.Status400BadRequest,
      StatusCode(StorageEndpointHandlers.GetMatchEvidenceArtifact("../secrets.json", matchEvidenceStore)));
    Assert.Equal(
      StatusCodes.Status400BadRequest,
      StatusCode(StorageEndpointHandlers.StoreMatchEvidenceArtifact(
        matchArtifact with { ArtifactRef = "match-api/../secrets.json" },
        matchEvidenceStore)));
    Assert.Equal(
      StatusCodes.Status400BadRequest,
      StatusCode(StorageEndpointHandlers.StoreMatchEvidenceArtifact(
        matchArtifact with { ArtifactRef = "match-api/invalid-hash.json", ContentHash = "sha256:rng-trace-api" },
        matchEvidenceStore)));

    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreCatalogSnapshot(catalog, catalogStore)));
    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreDeck(deck, deckStore)));
    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreResolvedRulesetProfile(profile, profileStore)));
    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreAssetManifest(asset, assetStore)));
    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreProductionAcceptanceReport(report, reportStore)));
    Assert.Equal(StatusCodes.Status409Conflict, StatusCode(StorageEndpointHandlers.StoreMatchEvidenceArtifact(matchArtifact, matchEvidenceStore)));
  }

  private static int? StatusCode(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

  private static CatalogSnapshot CreateCatalog(string snapshotId)
  {
    var card = CreateCard(snapshotId);
    return new CatalogSnapshot(
      snapshotId,
      "ruleset_mvp_core_v1",
      "sha256:resolved-api",
      FixedNow,
      [card]);
  }

  private static CardVersion CreateCard(string snapshotId) =>
    new(
      "card_arcane_apprentice",
      $"card_arcane_apprentice_{snapshotId}",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      snapshotId,
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:resolved-api",
      FixedNow,
      new CardDefinition(
        "card_arcane_apprentice",
        $"card_arcane_apprentice_{snapshotId}",
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        snapshotId,
        new StaticCardData(
          "Arcane Apprentice",
          CardType.MinionCard,
          ["mage"],
          Rarity.Common,
          2,
          ["APPRENTICE"],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(
          new MinionDefinition(2, 2, ["APPRENTICE"], [], CanAttack: true),
          Spell: null,
          Weapon: null,
          Location: null,
          HeroCard: null,
          HeroPower: null),
        Effects: [],
        new CardTextGeneration("Simple minion.", FlavorText: null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata("asset-api", "asset://visual-api", AudioManifestId: null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static ResolvedRulesetProfile CreateProfile(string profileId, string resolvedHash) =>
    new(
      profileId,
      "ruleset_mvp_core_v1",
      "0.2.0",
      FixedNow,
      "test",
      resolvedHash,
      "mvp",
      null,
      [],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      ["PLAY_CARD"],
      [],
      ["HAND"],
      ["ON_PLAY"],
      [],
      ["MINION_CARD"],
      ["MINION"],
      ["COMMON"],
      ["mage"],
      ["APPRENTICE"],
      new RulesetLimits(30, 25, 20, 80, 5, 1, "LIMITED", 3, 2, 3, 3, 3, 2, 2, 3, 1, 7, 10),
      new ValidationPolicyRef("validation", true, true, true, true, true, true, true, true, false),
      new BalancePolicyRef("balance", false, "MEDIUM", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
      new SimulationPolicyRef("simulation", [], [], 0, 0, false, 30, 80),
      new PublicationPolicyRef("publication", true, false, false, false, false, false, false, ["INTERNAL"]),
      new LegacyPolicy(false, false, false, false, true));

  private static AssetManifest CreateAssetManifest(string manifestId, CardVersion card) =>
    new(
      manifestId,
      "asset-manifest-bootstrap-0.1.0",
      card.CardId,
      card.CardVersionId,
      VisualAssetMode.StaticImage,
      AudioAssetMode.None,
      new VisualAssetManifest(
        "visual-api",
        "visual-v1",
        "asset://visual-api",
        "png",
        AssetModerationStatus.Passed,
        "sha256:visual-api"),
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      "sha256:asset-api");

  private static ProductionAcceptanceReport CreateAcceptanceReport(
    string reportId,
    DeckDefinition deck,
    AssetManifest asset) =>
    new(
      reportId,
      FixedNow,
      ProductionAcceptanceStatus.Accepted,
      [
        new AcceptedGeneratedCardEvidence(
          "card_arcane_apprentice",
          "card_arcane_apprentice_catalog-api",
          "generation-api",
          "generation-audit:generation-api",
          [asset.AssetManifestId])
      ],
      [
        new AcceptedDeckEvidence(
          deck.DeckId,
          deck.PlayerId,
          deck.Cards.Select(card => card.CardId).ToArray())
      ],
      new AcceptedMatchEvidence(
        "match-api",
        "simulation-api",
        "sha256:final-api",
        "HERO_DEFEATED",
        "player-1",
        CommandCount: 3,
        EventCount: 5,
        ReplayRuns: 2,
        ReplayVerified: true,
        ReconstructionTurnCount: 1,
        [asset.AssetManifestId],
        [
          new ExternalMatchEvidenceArtifact(
            "REPLAY_RNG_TRACE",
            "match-api/rng-trace.json",
            ValidMatchEvidenceHash)
        ]),
      new AcceptedProviderEvidence(
        ProductionCardGenerationStatus.Succeeded,
        Diagnostics: [],
        RedactedProviderRefs: ["provider:model:redacted"]),
      new ProductionReadinessEvidence(
        AcceptanceGateEvidence.Passed("L9_SECURITY", ["artifacts/security/e2e-009-abuse.json"]),
        AcceptanceGateEvidence.Passed("L10_RECOVERY", ["artifacts/recovery/e2e-007-restart.json"]),
        AcceptanceGateEvidence.Passed("L10_LOAD", ["artifacts/load/load-acceptance.json"]))
      {
        EvidenceReportRef = "readiness-evidence-api"
      },
      ResidualRisks: []);
}
