using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class CatalogCompatibilityValidatorTests
{
  [Fact]
  public void ValidateAcceptsSupportedCatalogSnapshot()
  {
    var snapshot = CreateSnapshot(CreateSupportedCard());
    var validator = new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator());

    var result = validator.Validate(new CatalogCompatibilityRequest(
      snapshot,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.True(result.IsCompatible);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void ValidateRejectsUnsupportedCardVersionAndSnapshotMismatches()
  {
    var unsupportedCard = CreateSupportedCard() with
    {
      Effects =
      [
        CreateSupportedEffect() with
        {
          Actions =
          [
            new AddCardToHandActionDefinition(
              "action_add_card_001",
              new CardSource("CARD_REF", "card:token"),
              PlayerSelector.Self,
              "FIZZLE_IF_FULL",
              null)
          ]
        }
      ]
    };
    var snapshot = CreateSnapshot(unsupportedCard, cardSnapshotId: "other-catalog", cardProfileHash: "other-hash");
    var validator = new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator());

    var result = validator.Validate(new CatalogCompatibilityRequest(
      snapshot,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry()));

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CATALOG_CARD_SNAPSHOT_MISMATCH"
      && diagnostic.Path == "snapshot.cards[0].catalogSnapshotId");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CATALOG_CARD_PROFILE_HASH_MISMATCH"
      && diagnostic.Path == "snapshot.cards[0].rulesetProfileHash");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ACTION"
      && diagnostic.Identifier == "card_arcane_apprentice_v1:ADD_CARD_TO_HAND");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CARD_REJECTED_BY_CAPABILITY_REGISTRY"
      && diagnostic.Identifier == "card_arcane_apprentice_v1:card_arcane_apprentice");
  }

  [Fact]
  public void ValidateRejectsRegistryHashAndCardVersionContractMismatches()
  {
    var card = CreateSupportedCard();
    var cardVersion = new CardVersion(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "card-dsl-future",
      "engine-future",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      card);
    var snapshot = new CatalogSnapshot(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [cardVersion]);
    var validator = new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator());

    var result = validator.Validate(new CatalogCompatibilityRequest(
      snapshot,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry(),
      "sha256:not-current"));

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CATALOG_ENGINE_CAPABILITY_REGISTRY_HASH_MISMATCH");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CATALOG_CARD_ENGINE_VERSION_MISMATCH"
      && diagnostic.Path == "snapshot.cards[0].engineVersion");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "CATALOG_CARD_SCHEMA_VERSION_MISMATCH"
      && diagnostic.Path == "snapshot.cards[0].schemaVersion");
  }

  private static CatalogSnapshot CreateSnapshot(
    CardDefinition card,
    string cardSnapshotId = "catalog-alpha",
    string cardProfileHash = "resolved-hash")
  {
    var cardVersion = new CardVersion(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      card.SchemaVersion,
      card.EngineVersion,
      cardSnapshotId,
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      cardProfileHash,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      card);

    return new CatalogSnapshot(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [cardVersion]);
  }

  private static CardDefinition CreateSupportedCard() =>
    new(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Arcane Apprentice",
        CardType.MinionCard,
        ["mage"],
        Rarity.Rare,
        2,
        ["APPRENTICE"],
        ["BATTLECRY"],
        Collectible: true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(2, 2, ["APPRENTICE"], [], CanAttack: true),
        null,
        null,
        null,
        null,
        null),
      [CreateSupportedEffect()],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "compatibility-test",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        "pipeline-v1",
        ["openspec/changes/catalog-publication-bootstrap/design.md"]));

  private static EffectDefinition CreateSupportedEffect() =>
    new(
      "effect_battlecry_damage",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      null,
      new TimingPolicy(Phase.OnPlay, null, 0, "SOURCE_ORDER"),
      [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.PlayerChosen,
        new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE_IF_INVALID"),
      [
        new DealDamageActionDefinition(
          "action_damage_001",
          new ValueExpression("FIXED", 3, null),
          new TargetRef("targetRequirement"),
          "ARCANE",
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("AUTO_VALID"),
      new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      ["BATTLECRY"]);
}
