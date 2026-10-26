using Game.Domain.Commands;
using Game.Domain.Mutation;
using Game.Domain.State;

namespace Game.Domain.Actions;

public sealed class EndTurnActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "END_TURN";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.Command is EndTurnCommand command
      && context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && !string.IsNullOrWhiteSpace(context.State.ActivePlayerId)
      && context.State.Players.ContainsKey(command.PlayerId)
      && StringComparer.Ordinal.Equals(context.State.ActivePlayerId, command.PlayerId);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not EndTurnCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(EndTurnCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var mutations = new List<StateMutation>();
    var expiringModifiers = CostModifierCalculator.ExpireAtEndOfTurnCostModifiers(context.State.Players[command.PlayerId]);
    foreach (var modifier in expiringModifiers)
    {
      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:end-turn:{command.PlayerId}:remove-modifier:{modifier.ModifierId}",
        RemoveModifierMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        "TURN_END",
        context.CommandSeq,
        null,
        $"end-turn:{command.PlayerId}:0000:remove-modifier:{modifier.ModifierId}",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["modifierId"] = modifier.ModifierId,
          ["playerId"] = command.PlayerId
        }));
    }

    mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:end-turn:{command.PlayerId}:advance-turn",
        AdvanceTurnMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        "TURN_END",
        context.CommandSeq,
        null,
        $"end-turn:{command.PlayerId}:0001",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["activePlayerId"] = command.PlayerId
        }));

    var nextPlayerId = GetNextPlayerId(context.State);
    var nextPlayer = context.State.Players[nextPlayerId];
    var drawCard = nextPlayer.Zones.Deck.FirstOrDefault();
    if (drawCard is not null)
    {
      var drawMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = drawCard.CardInstanceId,
        ["playerId"] = nextPlayerId
      };
      if (command.MaxHandSize.HasValue)
      {
        drawMetadata["maxHandSize"] = command.MaxHandSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        drawMetadata["destinationZone"] = nextPlayer.Zones.Hand.Count >= command.MaxHandSize.Value
          ? "GRAVEYARD"
          : "HAND";
      }

      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:end-turn:{command.PlayerId}:draw:{drawCard.CardInstanceId}",
        DrawCardMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        drawCard.CardInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        "TURN_END",
        context.CommandSeq,
        null,
        $"end-turn:{command.PlayerId}:0002",
        drawMetadata));
    }
    else
    {
      var fatigueDamage = nextPlayer.FatigueCounter + 1;
      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:end-turn:{command.PlayerId}:fatigue:{nextPlayerId}",
        ModifyFatigueMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        nextPlayerId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = 1
        },
        "TURN_END",
        context.CommandSeq,
        null,
        $"end-turn:{command.PlayerId}:0002",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["playerId"] = nextPlayerId
        }));

      if (nextPlayer.Hero is not null)
      {
        mutations.Add(new StateMutation(
          $"mutation:{context.CommandSeq}:end-turn:{command.PlayerId}:fatigue-damage:{nextPlayer.Hero.EntityInstanceId}",
          ApplyDamageMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          nextPlayer.Hero.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = fatigueDamage
          },
          "TURN_END",
          context.CommandSeq,
          null,
          $"end-turn:{command.PlayerId}:0003",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = nextPlayer.Hero.EntityInstanceId,
            ["preventable"] = bool.FalseString,
            ["playerId"] = nextPlayerId
          }));
      }
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:end-turn:{command.PlayerId}",
      context.CommandSeq,
      null,
      "TURN_END",
      $"command:{context.Command.Kind}",
      "END_TURN_ACTION_EXECUTOR",
      mutations,
      context.PreStateHash,
      null,
      null);
  }

  private static string GetNextPlayerId(Game.Domain.State.IGameState state)
  {
    if (ExtraTurnSchedule.TryGetNext(state.Counters, out var extraTurnPlayerId, out _)
      && state.Players.ContainsKey(extraTurnPlayerId))
    {
      return extraTurnPlayerId;
    }

    var orderedPlayerIds = state.Players.Keys.Order(StringComparer.Ordinal).ToArray();
    var currentIndex = Array.BinarySearch(orderedPlayerIds, state.ActivePlayerId, StringComparer.Ordinal);
    if (currentIndex < 0)
    {
      throw new InvalidOperationException($"Active player '{state.ActivePlayerId}' does not exist.");
    }

    return orderedPlayerIds[(currentIndex + 1) % orderedPlayerIds.Length];
  }
}
