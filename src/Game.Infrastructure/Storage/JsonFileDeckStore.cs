using Game.Application.Decks;
using Game.Shared.Decks;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileDeckStore : IDeckStore
{
  private readonly JsonFileCollectionStore<DeckDefinition> store;

  public JsonFileDeckStore(string rootDirectory)
  {
    store = new JsonFileCollectionStore<DeckDefinition>(
      Path.Combine(rootDirectory, "decks.json"),
      deck => deck.DeckId);
  }

  public DeckDefinition? Latest => store.Latest;

  public IReadOnlyList<DeckDefinition> Decks => store.Items;

  public DeckStoreResult Store(DeckDefinition deck) =>
    store.TryStore(deck)
      ? DeckStoreResult.Success
      : DeckStoreResult.DuplicateDeckId(deck.DeckId);

  public DeckDefinition? FindById(string deckId) => store.FindById(deckId);
}
