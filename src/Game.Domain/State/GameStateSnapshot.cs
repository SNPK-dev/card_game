namespace Game.Domain.State;

public sealed record GameStateSnapshot(
  int Revision,
  IReadOnlyDictionary<string, int> Counters,
  IReadOnlyDictionary<string, PlayerState> Players,
  IReadOnlyDictionary<string, PendingChoice> PendingChoices,
  string? ActivePlayerId,
  int TurnNumber,
  string Phase,
  GameTerminalState TerminalState);
