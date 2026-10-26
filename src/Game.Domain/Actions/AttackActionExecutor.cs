using Game.Domain.Commands;
using Game.Domain.Mutation;
using Game.Domain.State;

namespace Game.Domain.Actions;

public sealed class AttackActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "ATTACK";
  private const string TauntKeyword = "TAUNT";
  private const string StealthKeyword = "STEALTH";

  public string ActionType => SupportedActionType;

  public static string AttackCounterKey(
    int turnNumber,
    string playerId,
    string attackerEntityInstanceId) =>
    CombatStateKeys.AttackCounterKey(turnNumber, playerId, attackerEntityInstanceId);

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    return context.Command is AttackCommand command
      && context.CommandSeq > 0
      && !string.IsNullOrWhiteSpace(context.PreStateHash)
      && StringComparer.Ordinal.Equals(context.State.ActivePlayerId, command.PlayerId)
      && StringComparer.Ordinal.Equals(context.State.Phase, "TURN_START")
      && !StringComparer.Ordinal.Equals(command.PlayerId, command.TargetPlayerId)
      && TryGetAttacker(context.State, command, out var attacker)
      && IsAlive(attacker)
      && attacker.Attack > 0
      && !HasAttackedThisTurn(context.State, command)
      && (!IsBoardAttacker(attacker) || !WasSummonedThisTurn(context.State, command))
      && TryGetTarget(context.State, command, out var target)
      && IsAlive(target.Entity)
      && IsLegalTarget(context.State, command, target);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not AttackCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(AttackCommand)}.");
    }

    if (!CanExecute(context)
      || !TryGetAttacker(context.State, command, out var attacker)
      || !TryGetTarget(context.State, command, out var target))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var mutations = new List<StateMutation>
    {
      CreateDamageMutation(
        context,
        command,
        suffix: "damage-target",
        target.PlayerId,
        target.Entity.EntityInstanceId,
        attacker.Attack,
        orderingKey: "attack:0001")
    };

    if (target.IsBoardEntity && target.Entity.Attack > 0)
    {
      mutations.Add(CreateDamageMutation(
        context,
        command,
        suffix: "retaliate",
        command.PlayerId,
        IsWeaponAttacker(attacker)
          ? context.State.Players[command.PlayerId].Hero!.EntityInstanceId
          : attacker.EntityInstanceId,
        target.Entity.Attack,
        orderingKey: "attack:0002"));
    }

    if (IsWeaponAttacker(attacker))
    {
      mutations.Add(CreateEquippedEntityDamageMutation(
        context,
        command,
        attacker,
        amount: 1,
        orderingKey: "attack:0003"));

      if (attacker.Health - attacker.Damage <= 1)
      {
        mutations.Add(CreateEquippedEntityDestroyMutation(
          context,
          command,
          attacker,
          orderingKey: "attack:0004"));
      }
    }

    mutations.Add(new StateMutation(
      $"mutation:{context.CommandSeq}:attack:{command.AttackerEntityInstanceId}:mark-attacked",
      IncrementCounterMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      AttackCounterKey(context.State.TurnNumber, command.PlayerId, command.AttackerEntityInstanceId),
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "COMMAND",
      context.CommandSeq,
      null,
      "attack:0005"));

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:attack:{command.AttackerEntityInstanceId}",
      context.CommandSeq,
      null,
      "COMMAND",
      $"command:{context.Command.Kind}",
      "ATTACK_ACTION_EXECUTOR",
      mutations,
      context.PreStateHash,
      null,
      null);
  }

  private static StateMutation CreateDamageMutation(
    ActionExecutionContext context,
    AttackCommand command,
    string suffix,
    string targetPlayerId,
    string targetEntityInstanceId,
    int amount,
    string orderingKey) =>
    new(
      $"mutation:{context.CommandSeq}:attack:{command.AttackerEntityInstanceId}:{suffix}",
      ApplyDamageMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      targetEntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      "COMMAND",
      context.CommandSeq,
      null,
      orderingKey,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = targetEntityInstanceId,
        ["preventable"] = bool.TrueString,
        ["playerId"] = targetPlayerId
      });

  private static StateMutation CreateEquippedEntityDamageMutation(
    ActionExecutionContext context,
    AttackCommand command,
    EntityInstance attacker,
    int amount,
    string orderingKey) =>
    new(
      $"mutation:{context.CommandSeq}:attack:{command.AttackerEntityInstanceId}:damage-weapon",
      DamageEquippedEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      attacker.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      "COMMAND",
      context.CommandSeq,
      null,
      orderingKey,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = attacker.EntityInstanceId,
        ["playerId"] = command.PlayerId,
        ["slot"] = "WEAPON",
        ["sourceCardInstanceId"] = attacker.SourceCardInstanceId
      });

  private static StateMutation CreateEquippedEntityDestroyMutation(
    ActionExecutionContext context,
    AttackCommand command,
    EntityInstance attacker,
    string orderingKey) =>
    new(
      $"mutation:{context.CommandSeq}:attack:{command.AttackerEntityInstanceId}:destroy-weapon",
      DestroyEquippedEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      attacker.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "COMMAND",
      context.CommandSeq,
      null,
      orderingKey,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = attacker.EntityInstanceId,
        ["playerId"] = command.PlayerId,
        ["slot"] = "WEAPON",
        ["sourceCardInstanceId"] = attacker.SourceCardInstanceId
      });

  private static bool TryGetAttacker(
    IGameState state,
    AttackCommand command,
    out EntityInstance attacker)
  {
    attacker = default!;
    if (!state.Players.TryGetValue(command.PlayerId, out var player))
    {
      return false;
    }

    attacker = player.Zones.Board.SingleOrDefault(entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, command.AttackerEntityInstanceId))!;
    if (attacker is not null)
    {
      return true;
    }

    if (player.Weapon is not null
      && player.Hero is not null
      && IsAlive(player.Hero)
      && StringComparer.Ordinal.Equals(player.Weapon.EntityInstanceId, command.AttackerEntityInstanceId))
    {
      attacker = player.Weapon;
      return true;
    }

    return false;
  }

  private static bool TryGetTarget(
    IGameState state,
    AttackCommand command,
    out AttackTarget target)
  {
    target = default;
    if (!state.Players.TryGetValue(command.TargetPlayerId, out var player))
    {
      return false;
    }

    var boardTarget = player.Zones.Board.SingleOrDefault(entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, command.TargetEntityInstanceId));
    if (boardTarget is not null)
    {
      target = new AttackTarget(command.TargetPlayerId, boardTarget, IsBoardEntity: true);
      return true;
    }

    if (player.Hero is not null
      && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, command.TargetEntityInstanceId))
    {
      target = new AttackTarget(command.TargetPlayerId, player.Hero, IsBoardEntity: false);
      return true;
    }

    return false;
  }

  private static bool HasAttackedThisTurn(IGameState state, AttackCommand command) =>
    state.Counters.TryGetValue(
      AttackCounterKey(state.TurnNumber, command.PlayerId, command.AttackerEntityInstanceId),
      out var count)
    && count > 0;

  private static bool WasSummonedThisTurn(IGameState state, AttackCommand command) =>
    state.Counters.TryGetValue(
      CombatStateKeys.SummonCounterKey(command.PlayerId, command.AttackerEntityInstanceId),
      out var summonedTurn)
    && summonedTurn == state.TurnNumber;

  private static bool IsAlive(EntityInstance entity) => entity.Health - entity.Damage > 0;

  private static bool IsBoardAttacker(EntityInstance entity) =>
    entity.EntityType is Game.Shared.Primitives.EntityType.Minion;

  private static bool IsWeaponAttacker(EntityInstance entity) =>
    entity.EntityType is Game.Shared.Primitives.EntityType.Weapon;

  private static bool IsLegalTarget(
    IGameState state,
    AttackCommand command,
    AttackTarget target)
  {
    if (target.IsBoardEntity && HasKeyword(target.Entity, StealthKeyword))
    {
      return false;
    }

    if (!state.Players.TryGetValue(command.TargetPlayerId, out var defendingPlayer))
    {
      return false;
    }

    var requiredTaunts = defendingPlayer.Zones.Board
      .Where(entity => IsAlive(entity)
        && HasKeyword(entity, TauntKeyword)
        && !HasKeyword(entity, StealthKeyword))
      .ToArray();
    return requiredTaunts.Length == 0
      || target.IsBoardEntity && HasKeyword(target.Entity, TauntKeyword);
  }

  private static bool HasKeyword(EntityInstance entity, string keyword) =>
    (entity.Keywords ?? []).Contains(keyword, StringComparer.Ordinal);

  private readonly record struct AttackTarget(
    string PlayerId,
    EntityInstance Entity,
    bool IsBoardEntity);
}
