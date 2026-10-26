using Game.Domain.Commands;

namespace Game.Domain.Actions;

public sealed class GameplayCommandActionResolver : ICommandActionResolver
{
  public string ResolveActionType(IGameCommand command)
  {
    ArgumentNullException.ThrowIfNull(command);

    return command switch
    {
      ActivateLocationCommand => ActivateLocationActionExecutor.SupportedActionType,
      PlayCardCommand => PlayCardActionExecutor.SupportedActionType,
      AttackCommand => AttackActionExecutor.SupportedActionType,
      ConcedeCommand => ConcedeActionExecutor.SupportedActionType,
      EndTurnCommand => EndTurnActionExecutor.SupportedActionType,
      UseHeroPowerCommand => UseHeroPowerActionExecutor.SupportedActionType,
      ResolveChoiceCommand => ResolveChoiceActionExecutor.SupportedActionType,
      _ => CommandCounterActionExecutor.SupportedActionType
    };
  }
}
