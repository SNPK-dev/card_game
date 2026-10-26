namespace Game.Application.Catalog;

public sealed record CatalogCompatibilityDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
