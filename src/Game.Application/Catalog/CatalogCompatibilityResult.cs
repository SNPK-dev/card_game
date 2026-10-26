namespace Game.Application.Catalog;

public sealed record CatalogCompatibilityResult(
  bool IsCompatible,
  IReadOnlyList<CatalogCompatibilityDiagnostic> Diagnostics)
{
  public static CatalogCompatibilityResult Compatible() => new(true, []);

  public static CatalogCompatibilityResult Rejected(
    IReadOnlyList<CatalogCompatibilityDiagnostic> diagnostics) =>
    new(false, diagnostics);
}
