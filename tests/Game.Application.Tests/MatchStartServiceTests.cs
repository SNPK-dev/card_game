using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Matchmaking;
using Game.Application.Players;
using Game.Application.Rulesets;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class MatchStartServiceTests
{
  [Fact]
  public void StartCreatesInitialStateFromReadyDecks()
  {
    var context = CreateContext(
      [
        CreateDeck("deck-alpha", "player-2", [new DeckCardRef("card_arcane_apprentice", 2)]),
        CreateDeck("deck-beta", "player-1", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-alpha",
      ["deck-alpha", "deck-beta"]));

    Assert.True(result.Succeeded);
    Assert.Equal("game-alpha", result.GameId);
    var state = Assert.IsType<InMemoryGameState>(result.State);
    Assert.Equal("player-1", state.ActivePlayerId);
    Assert.Equal(1, state.TurnNumber);
    Assert.Equal("TURN_START", state.Phase);
    Assert.Equal(["player-1", "player-2"], state.Players.Keys);
    Assert.Empty(state.Players["player-1"].Zones.Deck);
    Assert.Equal("player-1:card_river_guard:001", state.Players["player-1"].Zones.Hand.Single().CardInstanceId);
    Assert.Equal(1, state.Players["player-1"].Mana);
    Assert.Equal(1, state.Players["player-1"].MaxMana);
    Assert.Empty(state.Players["player-2"].Zones.Deck);
    Assert.Equal(2, state.Players["player-2"].Zones.Hand.Count);
    Assert.Equal(0, state.Players["player-2"].Mana);
    Assert.NotNull(state.Players["player-1"].Hero);
    Assert.Equal(EntityType.Hero, state.Players["player-1"].Hero?.EntityType);
    Assert.Equal(30, state.Players["player-1"].Hero?.Health);
    Assert.Equal(2, state.Players["player-1"].HeroPower?.Cost);
    Assert.Equal("MAIN_PHASE", state.Players["player-1"].HeroPower?.ActivationTiming);
    Assert.Equal(
      state.Players["player-1"].Zones.SetAside.Single().CardInstanceId,
      state.Players["player-1"].HeroPower?.SourceCardInstanceId);
    Assert.Equal(CardType.HeroPowerCard, state.Players["player-1"].Zones.SetAside.Single().CardType);
    Assert.Equal(
      ["player-2:card_arcane_apprentice:001", "player-2:card_arcane_apprentice:002"],
      state.Players["player-2"].Zones.Hand.Select(card => card.CardInstanceId));
  }

  [Fact]
  public void StartInitializesDistinctStarterHeroPowersInSetAside()
  {
    var context = CreateContext(
      [
        CreateDeck(
          "deck-astra",
          "player-1",
          [new DeckCardRef("card_arcane_apprentice", 1)],
          StarterHeroProfiles.AstralWardenId),
        CreateDeck(
          "deck-varg",
          "player-2",
          [new DeckCardRef("card_river_guard", 1)],
          StarterHeroProfiles.CinderVanguardId)
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-starter-powers",
      ["deck-astra", "deck-varg"],
      "player-1"));

    Assert.True(result.Succeeded);
    var astra = result.State!.Players["player-1"];
    var varg = result.State.Players["player-2"];
    Assert.Contains("astral_restoration", astra.HeroPower!.SourceCardInstanceId, StringComparison.Ordinal);
    Assert.Contains("cinder_spark", varg.HeroPower!.SourceCardInstanceId, StringComparison.Ordinal);
    Assert.NotEqual(astra.HeroPower.SourceCardInstanceId, varg.HeroPower.SourceCardInstanceId);
    Assert.Single(astra.Zones.SetAside);
    Assert.Single(varg.Zones.SetAside);
  }

  [Fact]
  public void StartUsesTrustedStarterHeroStatsInsteadOfMutableDeckMetadata()
  {
    var tampered = CreateDeck(
      "deck-tampered",
      "player-1",
      [new DeckCardRef("card_arcane_apprentice", 1)]) with
    {
      StarterHeroName = "Forged hero",
      StarterHeroPortraitAssetRef = "hero://forged",
      StarterHeroHealth = 999
    };
    var context = CreateContext(
      [
        tampered,
        CreateDeck("deck-beta", "player-2", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-trusted-starter-profile",
      ["deck-tampered", "deck-beta"],
      "player-1"));

    Assert.True(result.Succeeded);
    Assert.Equal(StarterHeroProfiles.All[0].StartingHealth, result.State!.Players["player-1"].Hero!.Health);
    Assert.Equal("hero-card:astral-warden", result.State.Players["player-1"].Hero!.SourceCardInstanceId);
  }

  [Fact]
  public void StartRejectsUnknownStarterHeroProfile()
  {
    var unknownHeroDeck = CreateDeck(
      "deck-unknown-hero",
      "player-1",
      [new DeckCardRef("card_arcane_apprentice", 1)]) with
    {
      StarterHeroId = "unknown-hero"
    };
    var context = CreateContext(
      [
        unknownHeroDeck,
        CreateDeck("deck-beta", "player-2", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-unknown-starter-profile",
      ["deck-unknown-hero", "deck-beta"]));

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_START_STARTER_HERO_NOT_FOUND"
      && diagnostic.Identifier == "unknown-hero");
  }

  [Fact]
  public void StartUsesExplicitStartingPlayer()
  {
    var context = CreateContext(
      [
        CreateDeck("deck-alpha", "player-1", [new DeckCardRef("card_arcane_apprentice", 1)]),
        CreateDeck("deck-beta", "player-2", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-alpha",
      ["deck-alpha", "deck-beta"],
      "player-2"));

    Assert.True(result.Succeeded);
    Assert.Equal("player-2", result.State?.ActivePlayerId);
    Assert.Equal(1, result.State?.Players["player-2"].Mana);
    Assert.Equal(0, result.State?.Players["player-1"].Mana);
  }

  [Fact]
  public void StartDealsThreeCardsToStarterAndFourToOpponentDeterministically()
  {
    var context = CreateContext(
      [
        CreateDeck("deck-alpha", "player-1", [new DeckCardRef("card_arcane_apprentice", 2), new DeckCardRef("card_river_guard", 2)]),
        CreateDeck("deck-beta", "player-2", [new DeckCardRef("card_river_guard", 2), new DeckCardRef("card_arcane_apprentice", 2)])
      ]);

    var result = context.Service.Start(new MatchStartRequest("game-opening", ["deck-alpha", "deck-beta"], "player-1"));

    Assert.True(result.Succeeded);
    Assert.Equal(3, result.State!.Players["player-1"].Zones.Hand.Count);
    Assert.Single(result.State.Players["player-1"].Zones.Deck);
    Assert.Equal(4, result.State.Players["player-2"].Zones.Hand.Count);
    Assert.Empty(result.State.Players["player-2"].Zones.Deck);
    Assert.Equal("player-1:card_arcane_apprentice:001", result.State.Players["player-1"].Zones.Hand[0].CardInstanceId);
    Assert.Equal("player-2:card_river_guard:001", result.State.Players["player-2"].Zones.Hand[0].CardInstanceId);
  }

  [Fact]
  public void StartRejectsMissingDeckWithoutCreatingState()
  {
    var context = CreateContext(
      [CreateDeck("deck-alpha", "player-1", [new DeckCardRef("card_arcane_apprentice", 1)])]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-alpha",
      ["deck-alpha", "deck-missing"]));

    Assert.False(result.Succeeded);
    Assert.Null(result.State);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_DECK_NOT_FOUND"
      && diagnostic.Path == "deckIds[1].readiness.deckId");
  }

  [Fact]
  public void StartRejectsDuplicatePlayersAndUnknownStartingPlayer()
  {
    var context = CreateContext(
      [
        CreateDeck("deck-alpha", "player-1", [new DeckCardRef("card_arcane_apprentice", 1)]),
        CreateDeck("deck-beta", "player-1", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-alpha",
      ["deck-alpha", "deck-beta"],
      "player-3"));

    Assert.False(result.Succeeded);
    Assert.Null(result.State);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_START_DUPLICATE_PLAYER"
      && diagnostic.Identifier == "player-1");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "MATCH_START_PLAYER_NOT_FOUND"
      && diagnostic.Identifier == "player-3");
  }

  [Fact]
  public void StartRejectsNotReadyDeckDiagnostics()
  {
    var context = CreateContext(
      [
        CreateDeck("deck-alpha", "player-1", [new DeckCardRef("card_missing", 1)]),
        CreateDeck("deck-beta", "player-2", [new DeckCardRef("card_river_guard", 1)])
      ]);

    var result = context.Service.Start(new MatchStartRequest(
      "game-alpha",
      ["deck-alpha", "deck-beta"]));

    Assert.False(result.Succeeded);
    Assert.Null(result.State);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "DECK_CARD_NOT_IN_CATALOG"
      && diagnostic.Path == "deckIds[0].readiness.deckValidation.deck.cards[0].cardId");
  }

  private static TestContext CreateContext(IReadOnlyList<DeckDefinition> decks)
  {
    var deckStore = new InMemoryDeckStore();
    foreach (var deck in decks)
    {
      deckStore.Store(deck);
    }

    var catalogStore = new InMemoryCatalogSnapshotStore();
    catalogStore.Store(CreateCatalog());

    var profileStore = new InMemoryResolvedRulesetProfileStore();
    profileStore.Store(TestRulesetProfileFactory.CreateProfile("resolved-ruleset-mvp-core-v1"));

    var deckQuery = new DeckQueryService(deckStore);
    var catalogQuery = new CatalogSnapshotQueryService(catalogStore);
    var readiness = new MatchReadinessService(
      deckQuery,
      catalogQuery,
      new ResolvedRulesetProfileQueryService(profileStore),
      new DefaultEngineCapabilityRegistryProvider(),
      new DeckValidator(new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator())),
      new EngineCapabilityCompatibilityValidator(),
      new DefaultRulesetDefinitionRegistry());

    return new TestContext(new MatchStartService(deckQuery, catalogQuery, readiness));
  }

  private static DeckDefinition CreateDeck(
    string deckId,
    string playerId,
    IReadOnlyList<DeckCardRef> cards,
    string starterHeroId = StarterHeroProfiles.AstralWardenId)
  {
    var profile = StarterHeroProfiles.Find(starterHeroId)!;
    return new DeckDefinition(
      deckId,
      playerId,
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      cards,
      profile.HeroId,
      profile.DisplayName,
      profile.PortraitAssetRef,
      profile.StartingHealth);
  }

  private static CatalogSnapshot CreateCatalog() =>
    new(
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      [
        CreateCardVersion("card_arcane_apprentice", CardType.MinionCard),
        CreateCardVersion("card_river_guard", CardType.MinionCard)
      ]);

  private static CardVersion CreateCardVersion(string cardId, CardType cardType) =>
    new(
      cardId,
      $"{cardId}_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "resolved-hash",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      CreateCard(cardId, cardType));

  private static CardDefinition CreateCard(string cardId, CardType cardType) =>
    new(
      cardId,
      $"{cardId}_v1",
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "primitive-registry-bootstrap-0.2.0",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        cardId,
        cardType,
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
        "match-start-test",
        DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
        "pipeline-v1",
        ["openspec/changes/match-start-bootstrap/design.md"]));

  private sealed record TestContext(MatchStartService Service);
}
