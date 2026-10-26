namespace Game.Domain.Commands;

public enum CommandValidationCode
{
  InvalidCommandSequence = 0,
  MissingCommandType = 1,
  MissingPlayerId = 2,
  DebugCommandNotAllowed = 3,
  DebugReplayFlagNotAllowed = 4,
  IllegalCommand = 5
}
