using Game.Domain.Commands;

namespace Game.Domain.Actions;

public sealed class CommandCounterActionResolver : ICommandActionResolver
{
  public string ResolveActionType(IGameCommand command)
  {
    ArgumentNullException.ThrowIfNull(command);

    return CommandCounterActionExecutor.SupportedActionType;
  }
}
