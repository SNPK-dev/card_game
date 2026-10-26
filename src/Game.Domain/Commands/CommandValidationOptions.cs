namespace Game.Domain.Commands;

public sealed record CommandValidationOptions(bool AllowDebugReplay)
{
  public static CommandValidationOptions Production { get; } = new(false);

  public static CommandValidationOptions DebugReplay { get; } = new(true);
}
