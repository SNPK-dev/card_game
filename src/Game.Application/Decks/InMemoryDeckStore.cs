using Game.Shared.Decks;

namespace Game.Application.Decks;

public sealed class InMemoryDeckStore : IDeckStore
{
  private readonly List<DeckDefinition> decks = [];
  private readonly HashSet<string> deckIds = new(StringComparer.Ordinal);

  public DeckDefinition? Latest => decks.Count == 0 ? null : decks[^1];

  public IReadOnlyList<DeckDefinition> Decks => decks.ToArray();

  public DeckStoreResult Store(DeckDefinition deck)
  {
    ArgumentNullException.ThrowIfNull(deck);

    if (!deckIds.Add(deck.DeckId))
    {
      return DeckStoreResult.DuplicateDeckId(deck.DeckId);
    }

    decks.Add(deck);
    return DeckStoreResult.Success;
  }

  public DeckDefinition? FindById(string deckId) =>
    decks.FirstOrDefault(deck => string.Equals(deck.DeckId, deckId, StringComparison.Ordinal));
}
