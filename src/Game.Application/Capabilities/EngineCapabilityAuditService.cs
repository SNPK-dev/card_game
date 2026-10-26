using Game.Application.Catalog;

namespace Game.Application.Capabilities;

public sealed class EngineCapabilityAuditService(
  IEngineCapabilityCompatibilityValidator? compatibilityValidator = null,
  ICatalogCompatibilityValidator? catalogCompatibilityValidator = null,
  EngineCapabilityRegistryHasher? registryHasher = null) : IEngineCapabilityAuditService
{
  private readonly IEngineCapabilityCompatibilityValidator _compatibilityValidator =
    compatibilityValidator ?? new EngineCapabilityCompatibilityValidator();
  private readonly ICatalogCompatibilityValidator _catalogCompatibilityValidator =
    catalogCompatibilityValidator ?? new CatalogCompatibilityValidator(new EngineCapabilityCompatibilityValidator());
  private readonly EngineCapabilityRegistryHasher _registryHasher = registryHasher ?? new EngineCapabilityRegistryHasher();

  public EngineCapabilityAuditReport Audit(EngineCapabilityAuditRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Registry);

    var diagnostics = new List<EngineCapabilityAuditDiagnostic>();
    var registryHash = _registryHasher.ComputeHash(request.Registry);
    if (!string.IsNullOrWhiteSpace(request.ExpectedRegistryHash)
      && !StringComparer.Ordinal.Equals(request.ExpectedRegistryHash, registryHash))
    {
      diagnostics.Add(new EngineCapabilityAuditDiagnostic(
        "ENGINE_CAPABILITY_REGISTRY_HASH_MISMATCH",
        "engineCapabilityRegistry.hash",
        registryHash,
        "Engine capability registry hash does not match the expected pinned hash."));
    }

    AddCompatibilityDiagnostics(diagnostics, _compatibilityValidator.Validate(
      new EngineCapabilityCompatibilityRequest(
        request.Registry,
        request.RulesetProfile,
        request.CatalogSnapshot?.Cards.Select(card => card.Definition).ToArray() ?? [])));

    if (request.CatalogSnapshot is not null)
    {
      AddCatalogDiagnostics(diagnostics, _catalogCompatibilityValidator.Validate(
        new CatalogCompatibilityRequest(request.CatalogSnapshot, request.Registry)));

      if (request.RulesetProfile is not null
        && !StringComparer.Ordinal.Equals(request.CatalogSnapshot.RulesetProfileHash, request.RulesetProfile.ResolvedHash))
      {
        diagnostics.Add(new EngineCapabilityAuditDiagnostic(
          "CATALOG_RULESET_PROFILE_HASH_MISMATCH",
          "catalogSnapshot.rulesetProfileHash",
          request.CatalogSnapshot.RulesetProfileHash,
          "Catalog snapshot ruleset profile hash does not match the resolved ruleset profile hash."));
      }
    }

    var ordered = diagnostics
      .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
      .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
      .ThenBy(diagnostic => diagnostic.Identifier, StringComparer.Ordinal)
      .ToArray();

    return new EngineCapabilityAuditReport(ordered.Length == 0, registryHash, ordered);
  }

  private static void AddCompatibilityDiagnostics(
    ICollection<EngineCapabilityAuditDiagnostic> diagnostics,
    EngineCapabilityCompatibilityResult result)
  {
    foreach (var diagnostic in result.Diagnostics)
    {
      diagnostics.Add(new EngineCapabilityAuditDiagnostic(
        diagnostic.Code,
        $"capabilities.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message));
    }
  }

  private static void AddCatalogDiagnostics(
    ICollection<EngineCapabilityAuditDiagnostic> diagnostics,
    CatalogCompatibilityResult result)
  {
    foreach (var diagnostic in result.Diagnostics)
    {
      diagnostics.Add(new EngineCapabilityAuditDiagnostic(
        diagnostic.Code,
        $"catalog.{diagnostic.Path}",
        diagnostic.Identifier,
        diagnostic.Message));
    }
  }
}
