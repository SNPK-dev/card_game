using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class HealEntityActionExecutor(string playerId, string entityInstanceId, int amount) : IActionExecutor
{
  public const string SupportedActionType = "HEAL_ENTITY";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && amount > 0
      && context.State.Players.TryGetValue(playerId, out var player)
      && player.Zones.ContainsEntityOnBoard(entityInstanceId);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:heal-entity:{entityInstanceId}",
      context.CommandSeq,
      null,
      "ON_PLAY",
      $"command:{context.Command.Kind}",
      "HEAL_ENTITY_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:heal-entity:{entityInstanceId}",
          HealEntityMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          entityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = amount
          },
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"heal-entity:{entityInstanceId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = entityInstanceId,
            ["playerId"] = playerId
          })
      ],
      context.PreStateHash,
      null,
      null);
  }
}
