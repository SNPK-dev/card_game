using Game.Shared.Decks;
using Xunit;

namespace Game.Shared.Tests;

public sealed class DeckContractTests
{
  [Fact]
  public void DeckDefinitionPinsCatalogRulesetAndAggregateCardCounts()
  {
    var cardRef = new DeckCardRef("card_arcane_apprentice", 2);
    var deck = new DeckDefinition(
      "deck-alpha",
      "player-1",
      "catalog-alpha",
      "ruleset_mvp_core_v1",
      [cardRef]);

    Assert.Equal("deck-alpha", deck.DeckId);
    Assert.Equal("player-1", deck.PlayerId);
    Assert.Equal("catalog-alpha", deck.CatalogSnapshotId);
    Assert.Equal("ruleset_mvp_core_v1", deck.RulesetProfileId);
    Assert.Same(cardRef, deck.Cards.Single());
  }
}
