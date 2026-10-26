namespace Game.Application.Decks;

public sealed record DeckValidationDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
