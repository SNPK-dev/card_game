using Game.Application.Capabilities;
using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Rulesets;

namespace Game.Application.Matchmaking;

public sealed class MatchReadinessService(
  DeckQueryService deckQuery,
  CatalogSnapshotQueryService catalogQuery,
  ResolvedRulesetProfileQueryService profileQuery,
  IEngineCapabilityRegistryProvider capabilityProvider,
  IDeckValidator deckValidator,
  IEngineCapabilityCompatibilityValidator capabilityValidator,
  IRulesetDefinitionRegistry rulesetDefinitionRegistry) : IMatchReadinessService
{
  public MatchReadinessResult Check(MatchReadinessRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<MatchReadinessDiagnostic>();
    if (string.IsNullOrWhiteSpace(request.DeckId))
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        "MATCH_DECK_ID_REQUIRED",
        "deckId",
        string.Empty,
        "Required field 'deckId' is missing."));
      return MatchReadinessResult.NotReady(diagnostics);
    }

    var deck = deckQuery.GetDeck(request.DeckId);
    if (deck is null)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        "MATCH_DECK_NOT_FOUND",
        "deckId",
        request.DeckId,
        $"Deck '{request.DeckId}' was not found."));
      return MatchReadinessResult.NotReady(diagnostics);
    }

    var catalog = catalogQuery.GetSnapshot(deck.CatalogSnapshotId);
    if (catalog is null)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        "MATCH_CATALOG_SNAPSHOT_NOT_FOUND",
        "deck.catalogSnapshotId",
        deck.CatalogSnapshotId,
        $"Catalog snapshot '{deck.CatalogSnapshotId}' was not found."));
      return MatchReadinessResult.NotReady(diagnostics);
    }

    var profile = profileQuery.GetProfileBySourceAndHash(
      catalog.RulesetProfileId,
      catalog.RulesetProfileHash);
    if (profile is null)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        "MATCH_RULESET_PROFILE_NOT_FOUND",
        "catalog.rulesetProfileHash",
        catalog.RulesetProfileHash,
        $"Resolved ruleset profile '{catalog.RulesetProfileId}' with hash '{catalog.RulesetProfileHash}' was not found."));
      return MatchReadinessResult.NotReady(diagnostics);
    }

    var registry = capabilityProvider.GetCurrentRegistry();
    var registryHash = capabilityProvider.GetCurrentRegistryHash();
    var rulesetDefinition = rulesetDefinitionRegistry.Find(catalog.RulesetProfileId);
    if (rulesetDefinition is null)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        "INVALID_RULESET",
        "catalog.rulesetProfileId",
        catalog.RulesetProfileId,
        $"Ruleset '{catalog.RulesetProfileId}' is not registered in the backend ruleset definition registry."));
    }

    var deckValidation = deckValidator.Validate(new DeckValidationRequest(
      deck,
      catalog,
      profile,
      registry,
      registryHash,
      rulesetDefinition));
    AddDeckValidationDiagnostics(diagnostics, deckValidation);
    AddCapabilityDiagnostics(diagnostics, capabilityValidator.Validate(new EngineCapabilityCompatibilityRequest(
      registry,
      profile,
      [])));

    return diagnostics.Count == 0
      ? MatchReadinessResult.Ready(
        deckValidation.RulesetId,
        deckValidation.RulesetVersion,
        deckValidation.CatalogId,
        deckValidation.ExpectedDeckSize,
        deckValidation.ActualDeckSize)
      : MatchReadinessResult.NotReady(
        diagnostics,
        deckValidation.RulesetId,
        deckValidation.RulesetVersion,
        deckValidation.CatalogId,
        deckValidation.ExpectedDeckSize,
        deckValidation.ActualDeckSize,
        deckValidation.CopyLimitPassed,
        deckValidation.CardLegalityPassed);
  }

  private static void AddDeckValidationDiagnostics(
    ICollection<MatchReadinessDiagnostic> diagnostics,
    DeckValidationResult result)
  {
    foreach (var diagnostic in result.Diagnostics)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        diagnostic.Code,
        $"deckValidation.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message));
    }
  }

  private static void AddCapabilityDiagnostics(
    ICollection<MatchReadinessDiagnostic> diagnostics,
    EngineCapabilityCompatibilityResult result)
  {
    foreach (var diagnostic in result.Diagnostics)
    {
      diagnostics.Add(new MatchReadinessDiagnostic(
        diagnostic.Code,
        $"rulesetCapability.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message));
    }
  }
}
