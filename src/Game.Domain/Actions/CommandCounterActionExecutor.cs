using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class CommandCounterActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "COMMAND_COUNTER";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.Command.Kind)
      && !string.IsNullOrWhiteSpace(context.PreStateHash);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var counterKey = $"command:{context.Command.Kind}";
    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:{context.Command.Kind}",
      context.CommandSeq,
      null,
      "COMMAND",
      $"command:{context.Command.Kind}",
      "COMMAND_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:{context.Command.Kind}:increment-counter",
          IncrementCounterMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          counterKey,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 1
          },
          "COMMAND",
          context.CommandSeq,
          null,
          $"command:{context.Command.Kind}:0001")
      ],
      context.PreStateHash,
      null,
      null);
  }
}
