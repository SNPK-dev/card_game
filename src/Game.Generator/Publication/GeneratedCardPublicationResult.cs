using Game.Shared.Catalog;

namespace Game.Generator.Publication;

public sealed record GeneratedCardPublicationResult(
  bool Succeeded,
  CardVersion? CardVersion,
  CatalogSnapshot? CatalogSnapshot,
  IReadOnlyList<GeneratedCardPublicationDiagnostic> Diagnostics)
{
  public static GeneratedCardPublicationResult Success(
    CardVersion cardVersion,
    CatalogSnapshot catalogSnapshot) =>
    new(true, cardVersion, catalogSnapshot, []);

  public static GeneratedCardPublicationResult Failure(
    IReadOnlyList<GeneratedCardPublicationDiagnostic> diagnostics) =>
    new(false, null, null, diagnostics);
}
