using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Presentation;
using Game.Application.Rulesets;
using Game.Generator.Acceptance;
using Game.Generator.Artifacts;
using Game.Generator.Generation;
using Game.Infrastructure.Storage;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Integration.Tests;

public sealed class JsonFileStorageIntegrationTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-29T00:00:00Z");
  private const string ValidMatchEvidenceHash = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

  [Fact]
  public void JsonFileStoresPersistProductionArtifactsAcrossInstances()
  {
    var root = Path.Combine(Path.GetTempPath(), $"card-game-storage-{Guid.NewGuid():N}");
    try
    {
      var catalog = CreateCatalog("catalog-alpha");
      var deck = new DeckDefinition(
        "deck-alpha",
        "player-1",
        catalog.SnapshotId,
        "ruleset_mvp_core_v1",
        [new DeckCardRef("card_arcane_apprentice", 2)]);
      var profile = CreateProfile("resolved-alpha", "sha256:resolved-alpha");
      var asset = CreateAssetManifest("asset-alpha", catalog.Cards.Single());
      var report = CreateAcceptanceReport("acceptance-alpha", deck, asset);
      var binaryBytes = "binary artifact bytes"u8.ToArray();
      var binary = new BinaryArtifact(
        "asset-binary-alpha",
        JsonFileBinaryArtifactStore.ComputeHash(binaryBytes),
        "image/png",
        binaryBytes.Length,
        FixedNow);
      var matchArtifact = new MatchEvidenceArtifact(
        "match-alpha/rng-trace.json",
        "match-alpha",
        "REPLAY_RNG_TRACE",
        ValidMatchEvidenceHash,
        FixedNow);

      Assert.True(new JsonFileCatalogSnapshotStore(root).Store(catalog).Succeeded);
      Assert.True(new JsonFileDeckStore(root).Store(deck).Succeeded);
      Assert.True(new JsonFileResolvedRulesetProfileStore(root).Store(profile).Succeeded);
      Assert.True(new JsonFileAssetManifestStore(root).Store(asset).Succeeded);
      Assert.True(new JsonFileProductionAcceptanceReportStore(root).Store(report).Succeeded);
      Assert.True(new JsonFileBinaryArtifactStore(root).Store(binary, binaryBytes).Succeeded);
      Assert.True(new JsonFileMatchEvidenceArtifactStore(root).Store(matchArtifact).Succeeded);

      var reloadedCatalogStore = new JsonFileCatalogSnapshotStore(root);
      var reloadedDeckStore = new JsonFileDeckStore(root);
      var reloadedProfileStore = new JsonFileResolvedRulesetProfileStore(root);
      var reloadedAssetStore = new JsonFileAssetManifestStore(root);
      var reloadedReportStore = new JsonFileProductionAcceptanceReportStore(root);
      var reloadedBinaryStore = new JsonFileBinaryArtifactStore(root);
      var reloadedMatchEvidenceStore = new JsonFileMatchEvidenceArtifactStore(root);

      Assert.Equal(catalog.SnapshotId, new CatalogSnapshotQueryService(reloadedCatalogStore).GetLatestSnapshot()?.SnapshotId);
      Assert.Equal(deck.DeckId, new DeckQueryService(reloadedDeckStore).GetLatestDeck()?.DeckId);
      Assert.Equal(profile.Id, new ResolvedRulesetProfileQueryService(reloadedProfileStore).GetLatestProfile()?.Id);
      Assert.Equal(asset.AssetManifestId, new AssetManifestQueryService(reloadedAssetStore).GetLatestManifest()?.AssetManifestId);
      Assert.Equal(report.ReportId, reloadedReportStore.Latest?.ReportId);
      Assert.Equal(profile.Id, reloadedProfileStore.FindBySourceProfileAndHash("ruleset_mvp_core_v1", "sha256:resolved-alpha")?.Id);
      Assert.Equal(asset.ContentHash, reloadedAssetStore.FindById(asset.AssetManifestId)?.ContentHash);
      Assert.True(reloadedReportStore.FindById(report.ReportId)?.Match.ReplayVerified);
      Assert.True(reloadedReportStore.FindById(report.ReportId)?.ReadinessEvidence.IsAccepted);
      Assert.Equal("readiness-evidence-alpha", reloadedReportStore.FindById(report.ReportId)?.ReadinessEvidence.EvidenceReportRef);
      Assert.Equal(
        ["artifacts/recovery/e2e-007-restart.json"],
        reloadedReportStore.FindById(report.ReportId)?.ReadinessEvidence.Recovery.EvidenceRefs);
      Assert.Equal(binary.ContentHash, reloadedBinaryStore.FindByRef(binary.ArtifactRef)?.ContentHash);
      Assert.Equal(binaryBytes, reloadedBinaryStore.ReadBytes(binary.ArtifactRef));
      Assert.Equal(matchArtifact.ArtifactRef, reloadedMatchEvidenceStore.FindByMatchId("match-alpha").Single().ArtifactRef);

      Assert.False(reloadedCatalogStore.Store(catalog).Succeeded);
      Assert.False(reloadedDeckStore.Store(deck).Succeeded);
      Assert.False(reloadedProfileStore.Store(profile).Succeeded);
      Assert.False(reloadedAssetStore.Store(asset).Succeeded);
      Assert.False(reloadedReportStore.Store(report).Succeeded);
      Assert.False(reloadedBinaryStore.Store(binary, binaryBytes).Succeeded);
      Assert.False(reloadedMatchEvidenceStore.Store(matchArtifact).Succeeded);
      var invalidMatchArtifact = matchArtifact with { ArtifactRef = "../match-alpha/secrets.json" };
      var invalidResult = reloadedMatchEvidenceStore.Store(invalidMatchArtifact);
      Assert.False(invalidResult.Succeeded);
      Assert.StartsWith("INVALID_MATCH_EVIDENCE_ARTIFACT_REF:", invalidResult.ErrorCode, StringComparison.Ordinal);
      var invalidHashResult = reloadedMatchEvidenceStore.Store(
        matchArtifact with
        {
          ArtifactRef = "match-alpha/invalid-hash.json",
          ContentHash = "sha256:rng-trace-alpha"
        });
      Assert.False(invalidHashResult.Succeeded);
      Assert.StartsWith("INVALID_MATCH_EVIDENCE_CONTENT_HASH:", invalidHashResult.ErrorCode, StringComparison.Ordinal);
      Assert.Single(new JsonFileCatalogSnapshotStore(root).Snapshots);
      Assert.Single(new JsonFileDeckStore(root).Decks);
      Assert.Single(new JsonFileResolvedRulesetProfileStore(root).Profiles);
      Assert.Single(new JsonFileAssetManifestStore(root).Manifests);
      Assert.Single(new JsonFileProductionAcceptanceReportStore(root).Reports);
      Assert.Single(new JsonFileBinaryArtifactStore(root).Artifacts);
      Assert.Single(new JsonFileMatchEvidenceArtifactStore(root).Artifacts);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  private static CatalogSnapshot CreateCatalog(string snapshotId)
  {
    var card = CreateCard(snapshotId);
    return new CatalogSnapshot(
      snapshotId,
      "ruleset_mvp_core_v1",
      "sha256:resolved-alpha",
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
      "sha256:resolved-alpha",
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
        new AssetMetadata("asset-alpha", "asset://visual-alpha", AudioManifestId: null),
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
        "visual-alpha",
        "visual-v1",
        "asset://visual-alpha",
        "png",
        AssetModerationStatus.Passed,
        "sha256:visual-alpha"),
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, []),
      AssetModerationStatus.Passed,
      FixedNow,
      "sha256:asset-alpha");

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
          "card_arcane_apprentice_catalog-alpha",
          "generation-alpha",
          "generation-audit:generation-alpha",
          [asset.AssetManifestId])
      ],
      [
        new AcceptedDeckEvidence(
          deck.DeckId,
          deck.PlayerId,
          deck.Cards.Select(card => card.CardId).ToArray())
      ],
      new AcceptedMatchEvidence(
        "match-alpha",
        "simulation-alpha",
        "sha256:final-alpha",
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
            "match-alpha/rng-trace.json",
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
        EvidenceReportRef = "readiness-evidence-alpha"
      },
      ResidualRisks: []);
}
