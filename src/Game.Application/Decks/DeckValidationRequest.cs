using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Engine;
using Game.Shared.Ruleset;

namespace Game.Application.Decks;

public sealed record DeckValidationRequest(
  DeckDefinition Deck,
  CatalogSnapshot CatalogSnapshot,
  ResolvedRulesetProfile RulesetProfile,
  EngineCapabilityRegistry Registry,
  string? ExpectedRegistryHash = null,
  RulesetDefinition? RulesetDefinition = null);
