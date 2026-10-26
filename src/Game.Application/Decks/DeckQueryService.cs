using Game.Shared.Decks;

namespace Game.Application.Decks;

public sealed class DeckQueryService(IDeckStore store)
{
  public DeckDefinition? GetLatestDeck() => store.Latest;

  public DeckDefinition? GetDeck(string deckId) => store.FindById(deckId);
}
