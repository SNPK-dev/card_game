using Game.Domain.Mutation;
using Game.Domain.State;

namespace Game.Domain.Actions;

public sealed class SummonEntityActionExecutor(SummonEntityActionRequest request) : IActionExecutor
{
  public const string SupportedActionType = "SUMMON";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && request.Attack >= 0
      && request.Health > 0
      && context.State.Players.TryGetValue(request.PlayerId, out var player)
      && !player.Zones.ContainsEntityOnBoard(request.EntityInstanceId);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:summon-entity:{request.EntityInstanceId}",
      context.CommandSeq,
      null,
      "ON_SUMMON",
      $"command:{context.Command.Kind}",
      "SUMMON_ENTITY_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:summon-entity:{request.EntityInstanceId}",
          SummonEntityMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          request.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["attack"] = request.Attack,
            ["health"] = request.Health
          },
          "ON_SUMMON",
          context.CommandSeq,
          null,
          $"summon-entity:{request.EntityInstanceId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = request.EntityInstanceId,
            ["entityType"] = "MINION",
            ["playerId"] = request.PlayerId,
            ["sourceCardInstanceId"] = request.SourceCardInstanceId
          }),
        new StateMutation(
          $"mutation:{context.CommandSeq}:summon-entity:{request.EntityInstanceId}:mark-summoned-turn",
          IncrementCounterMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          CombatStateKeys.SummonCounterKey(request.PlayerId, request.EntityInstanceId),
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = context.State.TurnNumber
          },
          "ON_SUMMON",
          context.CommandSeq,
          null,
          $"summon-entity:{request.EntityInstanceId}:0002",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = request.EntityInstanceId,
            ["playerId"] = request.PlayerId
          })
      ],
      context.PreStateHash,
      null,
      null);
  }
}

public sealed record SummonEntityActionRequest(
  string PlayerId,
  string EntityInstanceId,
  string SourceCardInstanceId,
  int Attack,
  int Health);
