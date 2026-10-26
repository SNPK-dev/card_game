using Game.Application.Catalog;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class CatalogSnapshotStoreTests
{
  [Fact]
  public void StorePreservesSnapshotsAndReturnsLatestByInsertionOrder()
  {
    var first = CreateSnapshot("catalog-alpha");
    var second = CreateSnapshot("catalog-beta");
    var store = new InMemoryCatalogSnapshotStore();
    var query = new CatalogSnapshotQueryService(store);

    var firstResult = store.Store(first);
    var secondResult = store.Store(second);

    Assert.True(firstResult.Succeeded);
    Assert.True(secondResult.Succeeded);
    Assert.Equal(["catalog-alpha", "catalog-beta"], store.Snapshots.Select(snapshot => snapshot.SnapshotId));
    Assert.Same(second, store.Latest);
    Assert.Same(second, query.GetLatestSnapshot());
    Assert.Same(first, query.GetSnapshot("catalog-alpha"));
  }

  [Fact]
  public void StoreRejectsDuplicateSnapshotIdAndPreservesOriginal()
  {
    var original = CreateSnapshot("catalog-alpha", rulesetProfileHash: "hash-a");
    var duplicate = CreateSnapshot("catalog-alpha", rulesetProfileHash: "hash-b");
    var store = new InMemoryCatalogSnapshotStore();

    var firstResult = store.Store(original);
    var duplicateResult = store.Store(duplicate);

    Assert.True(firstResult.Succeeded);
    Assert.False(duplicateResult.Succeeded);
    Assert.Contains("catalog-alpha", duplicateResult.Error, StringComparison.Ordinal);
    Assert.Single(store.Snapshots);
    Assert.Same(original, store.FindById("catalog-alpha"));
    Assert.Equal("hash-a", store.FindById("catalog-alpha")?.RulesetProfileHash);
  }

  [Fact]
  public void CanonicalStarterSnapshotWinsOverNewerAllVanillaCatalog()
  {
    var store=new InMemoryCatalogSnapshotStore();
    var canonical=CreateStarterSnapshot(CatalogSnapshotQueryService.CanonicalStarterSnapshotId,true);
    var defective=CreateStarterSnapshot("catalog-newer-all-vanilla",false);
    store.Store(canonical);store.Store(defective);

    var query=new CatalogSnapshotQueryService(store);

    Assert.Same(defective,query.GetLatestSnapshot());
    Assert.Same(canonical,query.GetCanonicalStarterSnapshot());
  }

  private static CatalogSnapshot CreateSnapshot(
    string snapshotId,
    string rulesetProfileHash = "resolved-hash") =>
    new(
      snapshotId,
      "ruleset_mvp_core_v1",
      rulesetProfileHash,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [CreateCardVersion(snapshotId, rulesetProfileHash)]);

  private static CardVersion CreateCardVersion(
    string snapshotId,
    string rulesetProfileHash) =>
    new(
      "card_arcane_apprentice",
      $"card_arcane_apprentice_{snapshotId}",
      "dsl-v1",
      "engine-v1",
      snapshotId,
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      rulesetProfileHash,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      CreateCardDefinition(snapshotId));

  private static CardDefinition CreateCardDefinition(string snapshotId) =>
    new(
      "card_arcane_apprentice",
      $"card_arcane_apprentice_{snapshotId}",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      snapshotId,
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

  private static CatalogSnapshot CreateStarterSnapshot(string snapshotId,bool mechanics)
  {
    var cards=Enumerable.Range(1,15).Select(index=>
    {
      var version=CreateCardVersion(snapshotId,"resolved-hash");
      var id=$"starter_{index:00}";
      return version with{CardId=id,CardVersionId=$"{id}_v1",Definition=version.Definition with
      {
        CardId=id,CardVersion=$"{id}_v1",CatalogSnapshotId=snapshotId,
        Effects=mechanics?version.Definition.Effects:[],
        CardText=mechanics?version.Definition.CardText:new CardTextGeneration(string.Empty,null)
      }};
    }).ToArray();
    return new CatalogSnapshot(snapshotId,"ruleset_mvp_core_v1","resolved-hash",DateTimeOffset.Parse("2026-06-24T00:00:00Z"),cards);
  }
}
