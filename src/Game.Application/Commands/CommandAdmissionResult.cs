using Game.Domain.Commands;

namespace Game.Application.Commands;

public sealed class CommandAdmissionResult
{
  private CommandAdmissionResult(
    DeterministicCommandPayload? payload,
    IReadOnlyList<CommandValidationError> errors)
  {
    Payload = payload;
    Errors = errors;
  }

  public bool IsAccepted => Errors.Count == 0;

  public DeterministicCommandPayload? Payload { get; }

  public IReadOnlyList<CommandValidationError> Errors { get; }

  public static CommandAdmissionResult Accepted(DeterministicCommandPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);
    return new CommandAdmissionResult(payload, []);
  }

  public static CommandAdmissionResult Rejected(IReadOnlyList<CommandValidationError> errors)
  {
    ArgumentNullException.ThrowIfNull(errors);
    return new CommandAdmissionResult(null, errors);
  }
}
