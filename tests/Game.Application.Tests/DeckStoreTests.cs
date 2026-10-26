using Game.Application.Decks;
using Game.Shared.Decks;
using Xunit;

namespace Game.Application.Tests;

public sealed class DeckStoreTests
{
  [Fact]
  public void StorePreservesDecksAndReturnsLatestByInsertionOrder()
  {
    var first = CreateDeck("deck-alpha");
    var second = CreateDeck("deck-beta");
    var store = new InMemoryDeckStore();
    var query = new DeckQueryService(store);

    var firstResult = store.Store(first);
    var secondResult = store.Store(second);

    Assert.True(firstResult.Succeeded);
    Assert.True(secondResult.Succeeded);
    Assert.Equal(["deck-alpha", "deck-beta"], store.Decks.Select(deck => deck.DeckId));
    Assert.Same(second, store.Latest);
    Assert.Same(second, query.GetLatestDeck());
    Assert.Same(first, query.GetDeck("deck-alpha"));
  }

  [Fact]
  public void StoreRejectsDuplicateDeckIdAndPreservesOriginal()
  {
    var original = CreateDeck("deck-alpha", cardId: "card-original");
    var duplicate = CreateDeck("deck-alpha", cardId: "card-duplicate");
    var store = new InMemoryDeckStore();

    var firstResult = store.Store(original);
    var duplicateResult = store.Store(duplicate);

    Assert.True(firstResult.Succeeded);
    Assert.False(duplicateResult.Succeeded);
    Assert.Contains("deck-alpha", duplicateResult.Error, StringComparison.Ordinal);
    Assert.Single(store.Decks);
    Assert.Same(original, store.FindById("deck-alpha"));
    Assert.Equal("card-original", store.FindById("deck-alpha")?.Cards.Single().CardId);
  }

  private static DeckDefinition CreateDeck(
    string deckId,
    string cardId = "card_arcane_apprentice") =>
    new(
      deckId,
      "player-1",
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      [new DeckCardRef(cardId, 2)]);
}
