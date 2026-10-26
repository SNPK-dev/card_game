using Game.Domain.State;

namespace Game.Application.Matchmaking;

public sealed record MatchStartResult(
  bool Succeeded,
  string? GameId,
  IGameState? State,
  IReadOnlyList<MatchStartDiagnostic> Diagnostics)
{
  public static MatchStartResult Success(string gameId, IGameState state) =>
    new(true, gameId, state, []);

  public static MatchStartResult Rejected(IReadOnlyList<MatchStartDiagnostic> diagnostics) =>
    new(false, null, null, diagnostics);
}
