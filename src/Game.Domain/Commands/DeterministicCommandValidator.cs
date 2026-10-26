namespace Game.Domain.Commands;

public sealed class DeterministicCommandValidator : ICommandValidator
{
  public CommandValidationResult Validate(
    DeterministicCommandPayload payload,
    CommandValidationOptions options)
  {
    ArgumentNullException.ThrowIfNull(payload);
    ArgumentNullException.ThrowIfNull(options);

    var errors = new List<CommandValidationError>();

    if (payload.CommandSeq <= 0)
    {
      errors.Add(new CommandValidationError(
        CommandValidationCode.InvalidCommandSequence,
        payload.CommandSeq,
        "Command sequence must be greater than zero."));
    }

    if (string.IsNullOrWhiteSpace(payload.CommandType))
    {
      errors.Add(new CommandValidationError(
        CommandValidationCode.MissingCommandType,
        payload.CommandSeq,
        "Command type is required."));
    }

    if (RequiresPlayerId(payload.CommandCategory) && string.IsNullOrWhiteSpace(payload.PlayerId))
    {
      errors.Add(new CommandValidationError(
        CommandValidationCode.MissingPlayerId,
        payload.CommandSeq,
        "Player and choice commands require a player id."));
    }

    if (payload.CommandCategory == CommandCategory.Debug && !options.AllowDebugReplay)
    {
      errors.Add(new CommandValidationError(
        CommandValidationCode.DebugCommandNotAllowed,
        payload.CommandSeq,
        "Debug commands require debug replay admission."));
    }

    if (payload.CommandCategory != CommandCategory.Debug && payload.DebugReplayFlag)
    {
      errors.Add(new CommandValidationError(
        CommandValidationCode.DebugReplayFlagNotAllowed,
        payload.CommandSeq,
        "Debug replay flag is only valid for debug commands."));
    }

    return CommandValidationResult.Rejected(errors);
  }

  private static bool RequiresPlayerId(CommandCategory category)
  {
    return category is CommandCategory.Player or CommandCategory.Choice;
  }
}
