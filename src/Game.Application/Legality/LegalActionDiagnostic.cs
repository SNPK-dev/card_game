namespace Game.Application.Legality;

public sealed record LegalActionDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
