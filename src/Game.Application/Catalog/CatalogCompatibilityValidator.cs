using Game.Application.Capabilities;

namespace Game.Application.Catalog;

public sealed class CatalogCompatibilityValidator(
  IEngineCapabilityCompatibilityValidator cardCompatibilityValidator,
  Capabilities.EngineCapabilityRegistryHasher? registryHasher = null) : ICatalogCompatibilityValidator
{
  private readonly Capabilities.EngineCapabilityRegistryHasher _registryHasher =
    registryHasher ?? new Capabilities.EngineCapabilityRegistryHasher();

  public CatalogCompatibilityResult Validate(CatalogCompatibilityRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Snapshot);
    ArgumentNullException.ThrowIfNull(request.Registry);

    var diagnostics = new List<CatalogCompatibilityDiagnostic>();
    AddRequired(diagnostics, request.Snapshot.SnapshotId, "snapshot.snapshotId", "CATALOG_SNAPSHOT_ID_REQUIRED");
    AddRequired(diagnostics, request.Snapshot.RulesetProfileId, "snapshot.rulesetProfileId", "CATALOG_PROFILE_ID_REQUIRED");
    AddRequired(diagnostics, request.Snapshot.RulesetProfileHash, "snapshot.rulesetProfileHash", "CATALOG_PROFILE_HASH_REQUIRED");
    if (!string.IsNullOrWhiteSpace(request.ExpectedRegistryHash))
    {
      var actualRegistryHash = _registryHasher.ComputeHash(request.Registry);
      if (!string.Equals(request.ExpectedRegistryHash, actualRegistryHash, StringComparison.Ordinal))
      {
        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          "CATALOG_ENGINE_CAPABILITY_REGISTRY_HASH_MISMATCH",
          "engineCapabilityRegistry.hash",
          actualRegistryHash,
          "Catalog validation engine capability registry hash does not match the expected pinned hash."));
      }
    }

    for (var cardIndex = 0; cardIndex < request.Snapshot.Cards.Count; cardIndex++)
    {
      var cardVersion = request.Snapshot.Cards[cardIndex];
      var cardPath = $"snapshot.cards[{cardIndex}]";

      AddRequired(diagnostics, cardVersion.CardId, $"{cardPath}.cardId", "CATALOG_CARD_ID_REQUIRED");
      AddRequired(diagnostics, cardVersion.CardVersionId, $"{cardPath}.cardVersionId", "CATALOG_CARD_VERSION_ID_REQUIRED");
      if (!string.Equals(cardVersion.EngineVersion, cardVersion.Definition.EngineVersion, StringComparison.Ordinal))
      {
        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          "CATALOG_CARD_ENGINE_VERSION_MISMATCH",
          $"{cardPath}.engineVersion",
          cardVersion.EngineVersion,
          $"Card version '{cardVersion.CardVersionId}' engine version does not match its card definition."));
      }

      if (!string.Equals(cardVersion.SchemaVersion, cardVersion.Definition.SchemaVersion, StringComparison.Ordinal))
      {
        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          "CATALOG_CARD_SCHEMA_VERSION_MISMATCH",
          $"{cardPath}.schemaVersion",
          cardVersion.SchemaVersion,
          $"Card version '{cardVersion.CardVersionId}' schema version does not match its card definition."));
      }

      if (!string.Equals(cardVersion.CatalogSnapshotId, request.Snapshot.SnapshotId, StringComparison.Ordinal))
      {
        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          "CATALOG_CARD_SNAPSHOT_MISMATCH",
          $"{cardPath}.catalogSnapshotId",
          cardVersion.CatalogSnapshotId,
          $"Card version '{cardVersion.CardVersionId}' points at catalog snapshot '{cardVersion.CatalogSnapshotId}' but is stored in '{request.Snapshot.SnapshotId}'."));
      }

      if (!string.Equals(cardVersion.RulesetProfileHash, request.Snapshot.RulesetProfileHash, StringComparison.Ordinal))
      {
        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          "CATALOG_CARD_PROFILE_HASH_MISMATCH",
          $"{cardPath}.rulesetProfileHash",
          cardVersion.RulesetProfileHash,
          $"Card version '{cardVersion.CardVersionId}' profile hash does not match catalog snapshot hash."));
      }

      var cardResult = cardCompatibilityValidator.Validate(new EngineCapabilityCompatibilityRequest(
        request.Registry,
        null,
        [cardVersion.Definition]));

      foreach (var diagnostic in cardResult.Diagnostics)
      {
        var path = diagnostic.Path.StartsWith("cardDefinitions[0]", StringComparison.Ordinal)
          ? string.Concat($"{cardPath}.definition", diagnostic.Path.AsSpan("cardDefinitions[0]".Length))
          : $"{cardPath}.definition";

        diagnostics.Add(new CatalogCompatibilityDiagnostic(
          diagnostic.Code,
          path,
          $"{cardVersion.CardVersionId}:{diagnostic.Identifier}",
          diagnostic.Message));
      }
    }

    return diagnostics.Count == 0
      ? CatalogCompatibilityResult.Compatible()
      : CatalogCompatibilityResult.Rejected(diagnostics);
  }

  private static void AddRequired(
    ICollection<CatalogCompatibilityDiagnostic> diagnostics,
    string? value,
    string path,
    string code)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new CatalogCompatibilityDiagnostic(
      code,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }
}
