using Game.Domain.Commands;
using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class ConcedeActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "CONCEDE";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.Command is ConcedeCommand command
      && context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && context.State.Players.ContainsKey(command.PlayerId)
      && context.State.Players.Keys.Any(playerId =>
        !StringComparer.Ordinal.Equals(playerId, command.PlayerId));
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not ConcedeCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(ConcedeCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var winningPlayerId = context.State.Players.Keys
      .Where(playerId => !StringComparer.Ordinal.Equals(playerId, command.PlayerId))
      .Order(StringComparer.Ordinal)
      .First();

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:concede:{command.PlayerId}",
      context.CommandSeq,
      null,
      "COMMAND",
      $"command:{context.Command.Kind}",
      "CONCEDE_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:concede:{command.PlayerId}:set-terminal-state",
          SetTerminalStateMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          "game",
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "COMMAND",
          context.CommandSeq,
          null,
          "concede:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["losingPlayerId"] = command.PlayerId,
            ["message"] = $"Player '{command.PlayerId}' conceded.",
            ["reason"] = "CONCEDED",
            ["winningPlayerId"] = winningPlayerId
          })
      ],
      context.PreStateHash,
      null,
      null);
  }
}
