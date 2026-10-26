namespace Game.Domain.Commands;

public sealed class CommandValidationResult
{
  private CommandValidationResult(IReadOnlyList<CommandValidationError> errors)
  {
    Errors = errors;
  }

  public bool IsAccepted => Errors.Count == 0;

  public IReadOnlyList<CommandValidationError> Errors { get; }

  public static CommandValidationResult Accepted { get; } = new([]);

  public static CommandValidationResult Rejected(IEnumerable<CommandValidationError> errors)
  {
    ArgumentNullException.ThrowIfNull(errors);

    var materialized = errors.ToArray();
    if (materialized.Length == 0)
    {
      return Accepted;
    }

    return new CommandValidationResult(materialized);
  }
}
