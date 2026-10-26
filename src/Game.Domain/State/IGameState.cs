namespace Game.Domain.State;

public interface IGameState
{
  int Revision { get; }

  IReadOnlyDictionary<string, int> Counters { get; }

  IReadOnlyDictionary<string, PlayerState> Players { get; }

  IReadOnlyDictionary<string, string> PresentationMetadata { get; }

  IReadOnlyDictionary<string, PendingChoice> PendingChoices { get; }

  string? ActivePlayerId { get; }

  int TurnNumber { get; }

  string Phase { get; }

  GameTerminalState TerminalState { get; }

  GameStateSnapshot GetGameplaySnapshot();
}
