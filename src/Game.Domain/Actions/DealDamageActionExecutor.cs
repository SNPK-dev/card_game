using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class DealDamageActionExecutor(string playerId, string entityInstanceId, int amount) : IActionExecutor
{
  public const string SupportedActionType = "DEAL_DAMAGE";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && amount > 0
      && context.State.Players.TryGetValue(playerId, out var player)
      && (player.Zones.ContainsEntityOnBoard(entityInstanceId)
        || (player.Hero is not null
          && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, entityInstanceId)));
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:deal-damage:{entityInstanceId}",
      context.CommandSeq,
      null,
      "ON_PLAY",
      $"command:{context.Command.Kind}",
      "DEAL_DAMAGE_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:deal-damage:{entityInstanceId}",
          ApplyDamageMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          entityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = amount
          },
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"deal-damage:{entityInstanceId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = entityInstanceId,
            ["preventable"] = bool.TrueString,
            ["playerId"] = playerId
          })
      ],
      context.PreStateHash,
      null,
      null);
  }
}
