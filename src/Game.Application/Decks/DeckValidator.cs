using Game.Application.Catalog;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Ruleset;

namespace Game.Application.Decks;

public sealed class DeckValidator(
  ICatalogCompatibilityValidator catalogCompatibilityValidator) : IDeckValidator
{
  public DeckValidationResult Validate(DeckValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Deck);
    ArgumentNullException.ThrowIfNull(request.CatalogSnapshot);
    ArgumentNullException.ThrowIfNull(request.RulesetProfile);
    ArgumentNullException.ThrowIfNull(request.Registry);

    var diagnostics = new List<DeckValidationDiagnostic>();
    AddRequired(diagnostics, request.Deck.DeckId, "deck.deckId", "DECK_ID_REQUIRED");
    AddRequired(diagnostics, request.Deck.PlayerId, "deck.playerId", "DECK_PLAYER_ID_REQUIRED");
    AddRequired(diagnostics, request.Deck.CatalogSnapshotId, "deck.catalogSnapshotId", "DECK_CATALOG_SNAPSHOT_ID_REQUIRED");
    AddRequired(diagnostics, request.Deck.RulesetProfileId, "deck.rulesetProfileId", "DECK_RULESET_PROFILE_ID_REQUIRED");

    if (!string.Equals(request.Deck.CatalogSnapshotId, request.CatalogSnapshot.SnapshotId, StringComparison.Ordinal))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_CATALOG_SNAPSHOT_MISMATCH",
        "deck.catalogSnapshotId",
        request.Deck.CatalogSnapshotId,
        $"Deck points at catalog snapshot '{request.Deck.CatalogSnapshotId}' but was validated against '{request.CatalogSnapshot.SnapshotId}'."));
    }

    if (!string.Equals(request.Deck.RulesetProfileId, request.CatalogSnapshot.RulesetProfileId, StringComparison.Ordinal))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_CATALOG_RULESET_PROFILE_MISMATCH",
        "deck.rulesetProfileId",
        request.Deck.RulesetProfileId,
        $"Deck points at ruleset profile '{request.Deck.RulesetProfileId}' but catalog snapshot points at '{request.CatalogSnapshot.RulesetProfileId}'."));
    }

    if (!string.Equals(request.Deck.RulesetProfileId, request.RulesetProfile.SourceProfileId, StringComparison.Ordinal))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_RESOLVED_RULESET_PROFILE_MISMATCH",
        "deck.rulesetProfileId",
        request.Deck.RulesetProfileId,
        $"Deck points at ruleset profile '{request.Deck.RulesetProfileId}' but resolved profile source is '{request.RulesetProfile.SourceProfileId}'."));
    }

    if (!string.Equals(request.CatalogSnapshot.RulesetProfileHash, request.RulesetProfile.ResolvedHash, StringComparison.Ordinal))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_RESOLVED_RULESET_HASH_MISMATCH",
        "catalogSnapshot.rulesetProfileHash",
        request.CatalogSnapshot.RulesetProfileHash,
        "Catalog snapshot ruleset profile hash does not match the resolved ruleset profile hash."));
    }

    AddCardDiagnostics(diagnostics, request);
    AddProductionRulesetDiagnostics(diagnostics, request);
    AddCatalogCompatibilityDiagnostics(diagnostics, request);

    var actualDeckSize = request.Deck.Cards.Sum(card => Math.Max(card.Count, 0));
    var expectedDeckSize = request.RulesetDefinition?.DeckRules.DefaultDeckSize ?? 0;
    var rulesetId = request.RulesetDefinition?.RulesetId ?? string.Empty;
    var rulesetVersion = request.RulesetDefinition?.RulesetVersion ?? string.Empty;
    var copyLimitPassed = !diagnostics.Any(diagnostic =>
      string.Equals(diagnostic.Code, "CARD_COPY_LIMIT_EXCEEDED", StringComparison.Ordinal));
    var cardLegalityPassed = !diagnostics.Any(diagnostic =>
      diagnostic.Code is "CARD_NOT_FOUND"
        or "CARD_VERSION_NOT_FOUND"
        or "CARD_NOT_PUBLISHED"
        or "CARD_NOT_ALLOWED_BY_RULESET"
        or "CATALOG_RULESET_MISMATCH"
        or "INVALID_RULESET");

    return diagnostics.Count == 0
      ? DeckValidationResult.Valid(rulesetId, rulesetVersion, request.CatalogSnapshot.SnapshotId, expectedDeckSize, actualDeckSize)
      : DeckValidationResult.Rejected(
        diagnostics,
        rulesetId,
        rulesetVersion,
        request.CatalogSnapshot.SnapshotId,
        expectedDeckSize,
        actualDeckSize,
        copyLimitPassed,
        cardLegalityPassed);
  }

  private static void AddCardDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckValidationRequest request)
  {
    var catalogCards = request.CatalogSnapshot.Cards
      .Select(card => card.CardId)
      .ToHashSet(StringComparer.Ordinal);
    var seenCards = new HashSet<string>(StringComparer.Ordinal);

    for (var cardIndex = 0; cardIndex < request.Deck.Cards.Count; cardIndex++)
    {
      var card = request.Deck.Cards[cardIndex];
      var cardPath = $"deck.cards[{cardIndex}]";

      AddRequired(diagnostics, card.CardId, $"{cardPath}.cardId", "DECK_CARD_ID_REQUIRED");

      if (card.Count <= 0)
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "DECK_CARD_COUNT_INVALID",
          $"{cardPath}.count",
          card.CardId,
          $"Deck card '{card.CardId}' has non-positive count '{card.Count}'."));
      }

      if (string.IsNullOrWhiteSpace(card.CardId))
      {
        continue;
      }

      if (!seenCards.Add(card.CardId))
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "DECK_CARD_DUPLICATE",
          $"{cardPath}.cardId",
          card.CardId,
          $"Deck card '{card.CardId}' appears more than once; aggregate it into one card reference."));
      }

      if (!catalogCards.Contains(card.CardId))
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "DECK_CARD_NOT_IN_CATALOG",
          $"{cardPath}.cardId",
          card.CardId,
          $"Deck card '{card.CardId}' is absent from catalog snapshot '{request.CatalogSnapshot.SnapshotId}'."));
      }
    }
  }

  private static void AddProductionRulesetDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckValidationRequest request)
  {
    var ruleset = request.RulesetDefinition;
    if (ruleset is null)
    {
      return;
    }

    AddRulesetIdentityDiagnostics(diagnostics, request, ruleset);
    AddDeckSizeDiagnostics(diagnostics, request, ruleset);
    AddCatalogPolicyDiagnostics(diagnostics, request.CatalogSnapshot, ruleset);
    AddVersionedCardDiagnostics(diagnostics, request.Deck, request.CatalogSnapshot, ruleset);
  }

  private static void AddRulesetIdentityDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckValidationRequest request,
    RulesetDefinition ruleset)
  {
    if (string.IsNullOrWhiteSpace(ruleset.RulesetId)
      || string.IsNullOrWhiteSpace(ruleset.RulesetVersion)
      || ruleset.DeckRules.MinimumDeckSize <= 0
      || ruleset.DeckRules.MaximumDeckSize < ruleset.DeckRules.MinimumDeckSize
      || ruleset.DeckRules.DefaultDeckSize < ruleset.DeckRules.MinimumDeckSize
      || ruleset.DeckRules.DefaultDeckSize > ruleset.DeckRules.MaximumDeckSize
      || ruleset.DeckRules.MaximumCopiesPerCard <= 0)
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "INVALID_RULESET",
        "ruleset",
        ruleset.RulesetId,
        "Ruleset definition is missing required production deck constraints."));
    }

    if (ruleset.ProductionScale
      && (!string.Equals(ruleset.Environment, RulesetEnvironment.Production, StringComparison.Ordinal)
        || ruleset.DeckRules.DefaultDeckSize < 30))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "INVALID_RULESET",
        "ruleset.environment",
        ruleset.RulesetId,
        "Production-scale rulesets must be explicitly production and at least the full-scale deck size."));
    }

    if (!string.Equals(request.Deck.RulesetProfileId, ruleset.RulesetId, StringComparison.Ordinal)
      || !string.Equals(request.CatalogSnapshot.RulesetProfileId, ruleset.RulesetId, StringComparison.Ordinal))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "CATALOG_RULESET_MISMATCH",
        "ruleset.rulesetId",
        ruleset.RulesetId,
        "Deck, catalog, and backend ruleset definition must reference the same ruleset id."));
    }
  }

  private static void AddDeckSizeDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckValidationRequest request,
    RulesetDefinition ruleset)
  {
    var actualDeckSize = request.Deck.Cards.Sum(card => Math.Max(card.Count, 0));
    if (actualDeckSize < ruleset.DeckRules.MinimumDeckSize)
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_TOO_SMALL",
        "deck.cards",
        actualDeckSize.ToString(),
        $"Deck contains {actualDeckSize} cards but ruleset '{ruleset.RulesetId}' requires at least {ruleset.DeckRules.MinimumDeckSize}."));
    }

    if (actualDeckSize > ruleset.DeckRules.MaximumDeckSize)
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "DECK_TOO_LARGE",
        "deck.cards",
        actualDeckSize.ToString(),
        $"Deck contains {actualDeckSize} cards but ruleset '{ruleset.RulesetId}' allows at most {ruleset.DeckRules.MaximumDeckSize}."));
    }
  }

  private static void AddCatalogPolicyDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    CatalogSnapshot catalog,
    RulesetDefinition ruleset)
  {
    if (!ruleset.CardPoolPolicy.CatalogBound)
    {
      return;
    }

    if (!catalog.CompatibleRulesetIds.OrEmpty().Contains(ruleset.RulesetId, StringComparer.Ordinal)
      || !catalog.CompatibleRulesetVersions.OrEmpty().Contains(ruleset.RulesetVersion, StringComparer.Ordinal)
      || (ruleset.ProductionScale && !catalog.ProductionScaleCompatible))
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        "CATALOG_RULESET_MISMATCH",
        "catalog.compatibleRulesets",
        catalog.SnapshotId,
        $"Catalog snapshot '{catalog.SnapshotId}' is not declared compatible with ruleset '{ruleset.RulesetId}' version '{ruleset.RulesetVersion}'."));
    }
  }

  private static void AddVersionedCardDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckDefinition deck,
    CatalogSnapshot catalog,
    RulesetDefinition ruleset)
  {
    var catalogCards = catalog.Cards.ToDictionary(card => card.CardId, StringComparer.Ordinal);

    for (var cardIndex = 0; cardIndex < deck.Cards.Count; cardIndex++)
    {
      var deckCard = deck.Cards[cardIndex];
      if (string.IsNullOrWhiteSpace(deckCard.CardId))
      {
        continue;
      }

      var cardPath = $"deck.cards[{cardIndex}]";
      if (deckCard.Count > ruleset.DeckRules.MaximumCopiesPerCard)
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "CARD_COPY_LIMIT_EXCEEDED",
          $"{cardPath}.count",
          deckCard.CardId,
          $"Deck card '{deckCard.CardId}' has {deckCard.Count} copies but ruleset '{ruleset.RulesetId}' allows at most {ruleset.DeckRules.MaximumCopiesPerCard}."));
      }

      if (!catalogCards.TryGetValue(deckCard.CardId, out var catalogCard))
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "CARD_NOT_FOUND",
          $"{cardPath}.cardId",
          deckCard.CardId,
          $"Deck card '{deckCard.CardId}' is not present in catalog snapshot '{catalog.SnapshotId}'."));
        continue;
      }

      if (ruleset.CardPoolPolicy.RequirePublishedCardVersion)
      {
        if (string.IsNullOrWhiteSpace(deckCard.CardVersionId)
          || !string.Equals(deckCard.CardVersionId, catalogCard.CardVersionId, StringComparison.Ordinal))
        {
          diagnostics.Add(new DeckValidationDiagnostic(
            "CARD_VERSION_NOT_FOUND",
            $"{cardPath}.cardVersionId",
            deckCard.CardVersionId ?? string.Empty,
            $"Deck card '{deckCard.CardId}' must reference published card version '{catalogCard.CardVersionId}'."));
        }

        if (catalogCard.Published == false)
        {
          diagnostics.Add(new DeckValidationDiagnostic(
            "CARD_NOT_PUBLISHED",
            $"{cardPath}.cardVersionId",
            catalogCard.CardVersionId,
            $"Deck card '{deckCard.CardId}' references an unpublished card version."));
        }
      }

      if (!string.Equals(catalogCard.RulesetProfileId, ruleset.RulesetId, StringComparison.Ordinal)
        || !string.Equals(catalogCard.Definition.RulesetProfileId, ruleset.RulesetId, StringComparison.Ordinal))
      {
        diagnostics.Add(new DeckValidationDiagnostic(
          "CARD_NOT_ALLOWED_BY_RULESET",
          $"{cardPath}.cardId",
          deckCard.CardId,
          $"Deck card '{deckCard.CardId}' is not authored for ruleset '{ruleset.RulesetId}'."));
      }
    }
  }

  private void AddCatalogCompatibilityDiagnostics(
    ICollection<DeckValidationDiagnostic> diagnostics,
    DeckValidationRequest request)
  {
    var catalogResult = catalogCompatibilityValidator.Validate(new CatalogCompatibilityRequest(
      request.CatalogSnapshot,
      request.Registry,
      request.ExpectedRegistryHash));

    foreach (var diagnostic in catalogResult.Diagnostics)
    {
      diagnostics.Add(new DeckValidationDiagnostic(
        diagnostic.Code,
        $"catalog.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message));
    }
  }

  private static void AddRequired(
    ICollection<DeckValidationDiagnostic> diagnostics,
    string? value,
    string path,
    string code)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new DeckValidationDiagnostic(
      code,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }
}

file static class CollectionExtensions
{
  public static IReadOnlyList<string> OrEmpty(this IReadOnlyList<string>? value) => value ?? [];
}
