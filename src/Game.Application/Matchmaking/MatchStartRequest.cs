namespace Game.Application.Matchmaking;

public sealed record MatchStartRequest(
  string GameId,
  IReadOnlyList<string> DeckIds,
  string? StartingPlayerId = null);
