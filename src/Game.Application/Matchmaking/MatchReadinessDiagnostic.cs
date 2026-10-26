namespace Game.Application.Matchmaking;

public sealed record MatchReadinessDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
