using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public interface IActionExecutor
{
  string ActionType { get; }

  bool CanExecute(ActionExecutionContext context);

  ProposedMutationBatch Execute(ActionExecutionContext context);
}
