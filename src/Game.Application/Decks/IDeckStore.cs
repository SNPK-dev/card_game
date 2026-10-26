using Game.Shared.Decks;

namespace Game.Application.Decks;

public interface IDeckStore
{
  DeckDefinition? Latest { get; }

  IReadOnlyList<DeckDefinition> Decks { get; }

  DeckStoreResult Store(DeckDefinition deck);

  DeckDefinition? FindById(string deckId);
}
