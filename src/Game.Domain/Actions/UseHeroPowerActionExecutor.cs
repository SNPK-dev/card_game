using Game.Domain.Commands;
using Game.Domain.Mutation;

namespace Game.Domain.Actions;

public sealed class UseHeroPowerActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "USE_HERO_POWER";

  public string ActionType => SupportedActionType;

  public static string ActivationCounterKey(
    int turnNumber,
    string playerId,
    string sourceCardInstanceId) =>
    $"hero-power-used:{turnNumber}:{playerId}:{sourceCardInstanceId}";

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.Command is UseHeroPowerCommand command
      && context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && !string.IsNullOrWhiteSpace(command.SourceCardInstanceId)
      && command.ManaCost >= 0
      && StringComparer.Ordinal.Equals(context.State.ActivePlayerId, command.PlayerId)
      && StringComparer.Ordinal.Equals(context.State.Phase, "TURN_START")
      && context.State.Players.TryGetValue(command.PlayerId, out var player)
      && player.HeroPower is not null
      && StringComparer.Ordinal.Equals(player.HeroPower.SourceCardInstanceId, command.SourceCardInstanceId)
      && player.HeroPower.Cost == command.ManaCost
      && player.Mana >= command.ManaCost
      && HasValidTargetRefs(context.State, command)
      && !HasUsedHeroPowerThisTurn(context, command);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not UseHeroPowerCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(UseHeroPowerCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var mutations = new List<StateMutation>();
    if (command.ManaCost > 0)
    {
      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:use-hero-power:{command.SourceCardInstanceId}:pay-mana",
        ModifyManaMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = -command.ManaCost
        },
        "COMMAND",
        context.CommandSeq,
        null,
        "use-hero-power:0001",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["playerId"] = command.PlayerId,
          ["sourceCardInstanceId"] = command.SourceCardInstanceId
        }));
    }

    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["playerId"] = command.PlayerId,
      ["sourceCardInstanceId"] = command.SourceCardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(command.TargetPlayerId))
    {
      metadata["targetPlayerId"] = command.TargetPlayerId;
    }

    if (!string.IsNullOrWhiteSpace(command.TargetEntityInstanceId))
    {
      metadata["targetEntityInstanceId"] = command.TargetEntityInstanceId;
    }

    mutations.Add(new StateMutation(
      $"mutation:{context.CommandSeq}:use-hero-power:{command.SourceCardInstanceId}:mark-used",
      UseHeroPowerMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      ActivationCounterKey(context.State.TurnNumber, command.PlayerId, command.SourceCardInstanceId),
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "COMMAND",
      context.CommandSeq,
      null,
      "use-hero-power:0002",
      metadata));

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:use-hero-power:{command.SourceCardInstanceId}",
      context.CommandSeq,
      null,
      "COMMAND",
      $"command:{context.Command.Kind}",
      "USE_HERO_POWER_ACTION_EXECUTOR",
      mutations,
      context.PreStateHash,
      null,
      null);
  }

  private static bool HasUsedHeroPowerThisTurn(
    ActionExecutionContext context,
    UseHeroPowerCommand command) =>
    context.State.Counters.TryGetValue(
      ActivationCounterKey(context.State.TurnNumber, command.PlayerId, command.SourceCardInstanceId),
      out var count)
    && count > 0;

  private static bool HasValidTargetRefs(
    Game.Domain.State.IGameState state,
    UseHeroPowerCommand command)
  {
    var hasTargetPlayer = !string.IsNullOrWhiteSpace(command.TargetPlayerId);
    var hasTargetEntity = !string.IsNullOrWhiteSpace(command.TargetEntityInstanceId);
    if (!hasTargetPlayer && !hasTargetEntity)
    {
      return true;
    }

    if (!hasTargetPlayer || !hasTargetEntity
      || !state.Players.TryGetValue(command.TargetPlayerId, out var targetPlayer))
    {
      return false;
    }

    return targetPlayer.Zones.ContainsEntityOnBoard(command.TargetEntityInstanceId)
      || targetPlayer.Hero is not null
      && StringComparer.Ordinal.Equals(targetPlayer.Hero.EntityInstanceId, command.TargetEntityInstanceId);
  }
}
