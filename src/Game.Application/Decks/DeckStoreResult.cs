namespace Game.Application.Decks;

public sealed record DeckStoreResult(
  bool Succeeded,
  string? Error)
{
  public static DeckStoreResult Success { get; } = new(true, null);

  public static DeckStoreResult DuplicateDeckId(string deckId) =>
    new(false, $"Deck '{deckId}' already exists.");
}
