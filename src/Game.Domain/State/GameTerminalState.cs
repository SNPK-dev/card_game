namespace Game.Domain.State;

public sealed record GameTerminalState(
  bool IsTerminal,
  string Reason,
  string? WinningPlayerId,
  string? LosingPlayerId,
  string Message)
{
  public static GameTerminalState None { get; } = new(
    IsTerminal: false,
    Reason: "NONE",
    WinningPlayerId: null,
    LosingPlayerId: null,
    Message: string.Empty);
}
