using Game.Domain.Commands;

namespace Game.Domain.Actions;

public interface ICommandActionResolver
{
  string ResolveActionType(IGameCommand command);
}
