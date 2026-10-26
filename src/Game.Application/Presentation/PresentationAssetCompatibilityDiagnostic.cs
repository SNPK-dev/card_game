namespace Game.Application.Presentation;

public sealed record PresentationAssetCompatibilityDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
