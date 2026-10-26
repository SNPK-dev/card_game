namespace Game.Domain.Commands;

public interface ICommandValidator
{
  CommandValidationResult Validate(
    DeterministicCommandPayload payload,
    CommandValidationOptions options);
}
