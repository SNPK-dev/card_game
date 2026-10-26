using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class DrawCardActionExecutor(string playerId, string cardInstanceId) : IActionExecutor
{
  public const string SupportedActionType = "DRAW_CARD";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && context.State.Players.TryGetValue(playerId, out var player)
      && player.Zones.ContainsCardInZone(cardInstanceId, Game.Shared.Primitives.SourceZone.Deck);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:draw-card:{cardInstanceId}",
      context.CommandSeq,
      null,
      "ON_PLAY",
      $"command:{context.Command.Kind}",
      "DRAW_CARD_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:draw-card:{cardInstanceId}",
          DrawCardMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          cardInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["count"] = 1
          },
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"draw-card:{cardInstanceId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["cardInstanceId"] = cardInstanceId,
            ["playerId"] = playerId
          })
      ],
      context.PreStateHash,
      null,
      null);
  }
}
