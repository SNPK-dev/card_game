using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Matchmaking;
using Game.Application.Rulesets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class MatchReadinessServiceTests
{
  [Fact]
  public void CheckAcceptsStoredDeckCatalogAndResolvedProfile()
  {
    var context = CreateContext();

    var result = context.Service.Check(new MatchReadinessRequest("deck-alpha"));

    Assert.True(result.IsReady);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void CheckRejectsMissingDeckBeforeResolvingCatalogOrProfile()
  {
    var context = CreateContext(storeDeck: false);

    var result = context.Service.Check(new MatchReadinessRequest("deck-alpha"));

    Assert.False(result.IsReady);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_DECK_NOT_FOUND"
      && diagnostic.Path == "deckId");
  }

  [Fact]
  public void CheckRejectsMissingResolvedProfileByCatalogPin()
  {
    var context = CreateContext(storeProfile: false);

    var result = context.Service.Check(new MatchReadinessRequest("deck-alpha"));

    Assert.False(result.IsReady);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_RULESET_PROFILE_NOT_FOUND"
      && diagnostic.Path == "catalog.rulesetProfileHash");
  }

  [Fact]
  public void CheckMapsDeckValidationAndRulesetCapabilityDiagnostics()
  {
    var context = CreateContext(
      deck: CreateDeck(count: 0),
      profile: TestRulesetProfileFactory.CreateProfile(
        "resolved-ruleset-mvp-core-v1",
        allowedActions: ["ADD_CARD_TO_HAND"]));

    var result = context.Service.Check(new MatchReadinessRequest("deck-alpha"));

    Assert.False(result.IsReady);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CARD_COUNT_INVALID"
      && diagnostic.Path == "deckValidation.deck.cards[0].count");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "UNSUPPORTED_ACTION"
      && diagnostic.Path == "rulesetCapability.rulesetProfile.allowedActions");
  }

  private static TestContext CreateContext(
    bool storeDeck = true,
    bool storeCatalog = true,
    bool storeProfile = true,
    DeckDefinition? deck = null,
    CatalogSnapshot? catalog = null,
    ResolvedRulesetProfile? profile = null)
  {
    var deckStore = new InMemoryDeckStore();
    var catalogStore = new InMemoryCatalogSnapshotStore();
    var profileStore = new InMemoryResolvedRulesetProfileStore();

    if (storeDeck)
    {
      deckStore.Store(deck ?? CreateDeck());
    }

    if (storeCatalog)
    {
      catalogStore.Store(catalog ?? CreateSnapshot());
    }

    if (storeProfile)
    {
      profileStore.Store(profile ?? TestRulesetProfileFactory.CreateProfile("resolved-ruleset-mvp-core-v1"));
    }

    var service = new MatchReadinessService(
      new DeckQueryService(deckStore),
      new CatalogSnapshotQueryService(catalogStore),
      new ResolvedRulesetProfileQueryService(profileStore),
      new DefaultEngineCapabilityRegistryProvider(),
      new DeckValidator(new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator())),
      new EngineCapabilityCompatibilityValidator(),
      new Game.Application.Rulesets.DefaultRulesetDefinitionRegistry());

    return new TestContext(service);
  }

  private static DeckDefinition CreateDeck(int count = 1) =>
    new(
      "deck-alpha",
      "player-1",
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      [new DeckCardRef("card_arcane_apprentice", count)]);

  private static CatalogSnapshot CreateSnapshot() =>
    new(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [
        new CardVersion(
          "card_arcane_apprentice",
          "card_arcane_apprentice_v1",
          "card-dsl-0.2.0",
          "engine-bootstrap-0.3.0",
          "catalog-alpha",
          "pipeline-v1",
          "ruleset_mvp_core_v1",
          "resolved-hash",
          DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
          CreateCard())
      ]);

  private static CardDefinition CreateCard() =>
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
      [],
      new CardTextGeneration("Battlecry.", null),
      new BalanceMetadata(8, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata(
        "match-readiness-test",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        "pipeline-v1",
        ["openspec/changes/match-readiness-bootstrap/design.md"]));

  private sealed record TestContext(MatchReadinessService Service);
}
