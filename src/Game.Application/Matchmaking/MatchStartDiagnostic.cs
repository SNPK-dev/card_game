namespace Game.Application.Matchmaking;

public sealed record MatchStartDiagnostic(
  string Code,
  string Path,
  string Identifier,
  string Message);
