namespace Game.Domain.Commands;

public sealed record CommandValidationError(
  CommandValidationCode Code,
  int CommandSeq,
  string Message);
