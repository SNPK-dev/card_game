using Game.Domain.Commands;
using Game.Domain.Mutation;
using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Actions;

public sealed class ActivateLocationActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "ACTIVATE_LOCATION";

  public string ActionType => SupportedActionType;

  public static string ActivationCounterKey(
    int turnNumber,
    string playerId,
    string locationEntityInstanceId) =>
    $"location-used:{turnNumber}:{playerId}:{locationEntityInstanceId}";

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.Command is ActivateLocationCommand command
      && context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && !string.IsNullOrWhiteSpace(command.LocationEntityInstanceId)
      && !string.IsNullOrWhiteSpace(command.SourceCardInstanceId)
      && StringComparer.Ordinal.Equals(context.State.ActivePlayerId, command.PlayerId)
      && StringComparer.Ordinal.Equals(context.State.Phase, "TURN_START")
      && context.State.Players.TryGetValue(command.PlayerId, out var player)
      && player.Location is not null
      && StringComparer.Ordinal.Equals(player.Location.EntityInstanceId, command.LocationEntityInstanceId)
      && StringComparer.Ordinal.Equals(player.Location.SourceCardInstanceId, command.SourceCardInstanceId)
      && IsAlive(player.Location)
      && HasValidTargetRefs(context.State, command)
      && !HasUsedLocationThisTurn(context, command);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not ActivateLocationCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(ActivateLocationCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var location = context.State.Players[command.PlayerId].Location!;
    var mutations = new List<StateMutation>
    {
      CreateUseLocationMutation(context, command),
      CreateDurabilityMutation(context, command, location)
    };

    if (location.Health - location.Damage <= 1)
    {
      mutations.Add(CreateDestroyLocationMutation(context, command, location));
    }

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:activate-location:{command.LocationEntityInstanceId}",
      context.CommandSeq,
      null,
      "COMMAND",
      $"command:{context.Command.Kind}",
      "ACTIVATE_LOCATION_ACTION_EXECUTOR",
      mutations,
      context.PreStateHash,
      null,
      null);
  }

  private static StateMutation CreateUseLocationMutation(
    ActionExecutionContext context,
    ActivateLocationCommand command)
  {
    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["locationEntityInstanceId"] = command.LocationEntityInstanceId,
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

    return new StateMutation(
      $"mutation:{context.CommandSeq}:activate-location:{command.LocationEntityInstanceId}:mark-used",
      UseLocationMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      ActivationCounterKey(context.State.TurnNumber, command.PlayerId, command.LocationEntityInstanceId),
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "COMMAND",
      context.CommandSeq,
      null,
      "activate-location:0001",
      metadata);
  }

  private static StateMutation CreateDurabilityMutation(
    ActionExecutionContext context,
    ActivateLocationCommand command,
    EntityInstance location) =>
    new(
      $"mutation:{context.CommandSeq}:activate-location:{command.LocationEntityInstanceId}:damage-location",
      DamageEquippedEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      command.LocationEntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "COMMAND",
      context.CommandSeq,
      null,
      "activate-location:0002",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = command.LocationEntityInstanceId,
        ["playerId"] = command.PlayerId,
        ["slot"] = "LOCATION",
        ["sourceCardInstanceId"] = location.SourceCardInstanceId
      });

  private static StateMutation CreateDestroyLocationMutation(
    ActionExecutionContext context,
    ActivateLocationCommand command,
    EntityInstance location) =>
    new(
      $"mutation:{context.CommandSeq}:activate-location:{command.LocationEntityInstanceId}:destroy-location",
      DestroyEquippedEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      command.LocationEntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "COMMAND",
      context.CommandSeq,
      null,
      "activate-location:0003",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = command.LocationEntityInstanceId,
        ["playerId"] = command.PlayerId,
        ["slot"] = "LOCATION",
        ["sourceCardInstanceId"] = location.SourceCardInstanceId
      });

  private static bool HasUsedLocationThisTurn(
    ActionExecutionContext context,
    ActivateLocationCommand command) =>
    context.State.Counters.TryGetValue(
      ActivationCounterKey(context.State.TurnNumber, command.PlayerId, command.LocationEntityInstanceId),
      out var count)
    && count > 0;

  private static bool HasValidTargetRefs(
    IGameState state,
    ActivateLocationCommand command)
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

  private static bool IsAlive(EntityInstance entity) => entity.Health - entity.Damage > 0;
}
