using Game.Domain.Commands;
using Game.Domain.State;

namespace Game.Domain.Actions;

public sealed record ActionExecutionContext(
  IGameState State,
  IGameCommand Command,
  int CommandSeq,
  string PreStateHash);
