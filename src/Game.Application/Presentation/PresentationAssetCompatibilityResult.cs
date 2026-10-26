namespace Game.Application.Presentation;

public sealed record PresentationAssetCompatibilityResult(
  bool IsCompatible,
  IReadOnlyList<PresentationAssetCompatibilityDiagnostic> Diagnostics)
{
  public static PresentationAssetCompatibilityResult Compatible() => new(true, []);

  public static PresentationAssetCompatibilityResult Rejected(
    IReadOnlyList<PresentationAssetCompatibilityDiagnostic> diagnostics) => new(false, diagnostics);
}
