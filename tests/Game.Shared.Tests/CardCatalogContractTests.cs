using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Shared.Tests;

public sealed class CardCatalogContractTests
{
  [Fact]
  public void CatalogSnapshotPinsRulesetProfileHashAndCardVersions()
  {
    var cardVersion = new CardVersion(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      CreateCardDefinition());
    var snapshot = new CatalogSnapshot(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [cardVersion]);

    Assert.Equal("catalog-alpha", snapshot.SnapshotId);
    Assert.Equal("ruleset_mvp_core_v1", snapshot.RulesetProfileId);
    Assert.Equal("resolved-hash", snapshot.RulesetProfileHash);
    Assert.Same(cardVersion, snapshot.Cards.Single());
  }

  private static CardDefinition CreateCardDefinition() =>
    new(
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
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
      [],
      new CardTextGeneration("Battlecry: Deal 3 damage.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-23T00:00:00Z"),
        "pipeline-v1",
        ["docs/generator/dsl_reference.md"]));
}
