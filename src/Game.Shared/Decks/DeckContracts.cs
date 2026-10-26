namespace Game.Shared.Decks;

public sealed record DeckDefinition(
  string DeckId,
  string PlayerId,
  string CatalogSnapshotId,
  string RulesetProfileId,
  IReadOnlyList<DeckCardRef> Cards,
  string StarterHeroId = "astral-warden",
  string StarterHeroName = "Astra, Astral Warden",
  string StarterHeroPortraitAssetRef = "hero://astral-warden",
  int StarterHeroHealth = 30);

public sealed record DeckCardRef(
  string CardId,
  int Count,
  string? CardVersionId = null);
