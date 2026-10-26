using Game.Domain.Commands;
using Game.Domain.Mutation;
using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Actions;

public sealed class PlayCardActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "PLAY_CARD";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    if (context.Command is not PlayCardCommand command
      || context.CommandSeq <= 0
      || string.IsNullOrWhiteSpace(context.PreStateHash)
      || command.Attack < 0
      || command.Health < 0
      || command.ManaCost < 0
      || command.CostReduction < 0
      || !StringComparer.Ordinal.Equals(context.State.ActivePlayerId, command.PlayerId)
      || !StringComparer.Ordinal.Equals(context.State.Phase, "TURN_START")
      || !context.State.Players.TryGetValue(command.PlayerId, out var player)
      || player.Mana < command.ManaCost
      || command.CostReduction > CostModifierCalculator.CalculateReduction(player, command.ManaCost + command.CostReduction))
    {
      return false;
    }

    var card = player.Zones.Hand.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.CardInstanceId, command.CardInstanceId));
    if (card is null)
    {
      return false;
    }

    return card.CardType switch
    {
      CardType.MinionCard or CardType.TokenCard =>
        command.Health > 0
        && command.Durability == 0
        && !string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && !player.Zones.ContainsEntityOnBoard(command.EntityInstanceId),
      CardType.SpellCard =>
        command.Attack == 0
        && command.Health == 0
        && command.Durability == 0
        && string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && command.MaxBoardSize is null
        && (command.Keywords is null || command.Keywords.Count == 0),
      CardType.WeaponCard =>
        command.Attack >= 0
        && command.Health == 0
        && command.Durability > 0
        && !string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && command.MaxBoardSize is null,
      CardType.LocationCard =>
        command.Attack == 0
        && command.Health == 0
        && command.Durability > 0
        && !string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && command.MaxBoardSize is null
        && (command.Keywords is null || command.Keywords.Count == 0),
      CardType.HeroCard =>
        command.Attack == 0
        && command.Health == 0
        && command.Durability == 0
        && command.ArmorGain >= 0
        && string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && command.MaxBoardSize is null
        && (command.Keywords is null || command.Keywords.Count == 0)
        && (!command.HeroPowerCost.HasValue || !string.IsNullOrWhiteSpace(command.HeroPowerActivationTiming)),
      CardType.HeroPowerCard =>
        command.Attack == 0
        && command.Health == 0
        && command.Durability == 0
        && command.ArmorGain == 0
        && string.IsNullOrWhiteSpace(command.EntityInstanceId)
        && command.MaxBoardSize is null
        && (command.Keywords is null || command.Keywords.Count == 0)
        && command.HeroPowerCost.HasValue
        && !string.IsNullOrWhiteSpace(command.HeroPowerActivationTiming),
      _ => false
    };
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not PlayCardCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(PlayCardCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var player = context.State.Players[command.PlayerId];
    var card = player.Zones.Hand.Single(candidate =>
      StringComparer.Ordinal.Equals(candidate.CardInstanceId, command.CardInstanceId));
    var isBoardEntityPlay = card.CardType is CardType.MinionCard or CardType.TokenCard;
    var isSetAsideEntityPlay = isBoardEntityPlay || card.CardType is CardType.WeaponCard or CardType.LocationCard or CardType.HeroCard or CardType.HeroPowerCard;
    var mutations = new List<StateMutation>();
    if (command.ManaCost > 0)
    {
      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:pay-mana",
        ModifyManaMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = -command.ManaCost
        },
        "ON_PLAY",
        context.CommandSeq,
        null,
        $"play-card:{command.CardInstanceId}:0000",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = command.CardInstanceId,
          ["playerId"] = command.PlayerId
        }));
    }

    mutations.Add(new StateMutation(
      $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:move-card",
      MoveCardZoneMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      command.CardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "ON_PLAY",
      context.CommandSeq,
      null,
      $"play-card:{command.CardInstanceId}:0001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = command.CardInstanceId,
        ["fromZone"] = "HAND",
        ["playerId"] = command.PlayerId,
        ["toZone"] = isSetAsideEntityPlay ? "SET_ASIDE" : "GRAVEYARD"
      }));
    if (command.CostReduction > 0)
    {
      var consumedModifiers = CostModifierCalculator
        .ConsumedOnPlayCostModifiers(context.State.Players[command.PlayerId]);
      foreach (var modifier in consumedModifiers)
      {
        mutations.Add(new StateMutation(
          $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:remove-modifier:{modifier.ModifierId}",
          RemoveModifierMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          command.PlayerId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"play-card:{command.CardInstanceId}:0001:remove-modifier:{modifier.ModifierId}",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["cardInstanceId"] = command.CardInstanceId,
            ["modifierId"] = modifier.ModifierId,
            ["playerId"] = command.PlayerId
          }));
      }
    }

    if (card.CardType is CardType.WeaponCard)
    {
      if (player.Weapon is not null)
      {
        mutations.Add(CreateDestroyEquippedEntityMutation(
          context,
          command.CardInstanceId,
          player.Weapon,
          "replace-weapon",
          "0002"));
      }

      var weaponMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = command.EntityInstanceId,
        ["entityType"] = "WEAPON",
        ["playerId"] = command.PlayerId,
        ["sourceCardInstanceId"] = command.CardInstanceId
      };
      foreach (var keyword in (command.Keywords ?? []).Order(StringComparer.Ordinal).Select((value, index) => new { value, index }))
      {
        weaponMetadata[$"entityKeyword:{keyword.index + 1:0000}"] = keyword.value;
      }

      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:equip-weapon",
        EquipWeaponMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.EntityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = command.Attack,
          ["durability"] = command.Durability
        },
        "ON_PLAY",
        context.CommandSeq,
        null,
        $"play-card:{command.CardInstanceId}:{(player.Weapon is null ? "0002" : "0003")}",
        weaponMetadata));

      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
        context.CommandSeq,
        null,
        "ON_PLAY",
        $"command:{context.Command.Kind}",
        "PLAY_CARD_ACTION_EXECUTOR",
        mutations,
        context.PreStateHash,
        null,
        null);
    }

    if (card.CardType is CardType.LocationCard)
    {
      if (player.Location is not null)
      {
        mutations.Add(CreateDestroyEquippedEntityMutation(
          context,
          command.CardInstanceId,
          player.Location,
          "replace-location",
          "0002"));
      }

      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:create-location",
        CreateLocationMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.EntityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["durability"] = command.Durability
        },
        "ON_PLAY",
        context.CommandSeq,
        null,
        $"play-card:{command.CardInstanceId}:{(player.Location is null ? "0002" : "0003")}",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["entityInstanceId"] = command.EntityInstanceId,
          ["entityType"] = "LOCATION",
          ["playerId"] = command.PlayerId,
          ["sourceCardInstanceId"] = command.CardInstanceId
        }));

      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
        context.CommandSeq,
        null,
        "ON_PLAY",
        $"command:{context.Command.Kind}",
        "PLAY_CARD_ACTION_EXECUTOR",
        mutations,
        context.PreStateHash,
        null,
        null);
    }

    if (card.CardType is CardType.HeroCard)
    {
      var heroCardPayload = new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["armorGain"] = command.ArmorGain
      };
      var heroCardMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = command.CardInstanceId,
        ["playerId"] = command.PlayerId
      };
      if (command.HeroPowerCost.HasValue)
      {
        heroCardPayload["heroPowerCost"] = command.HeroPowerCost.Value;
        heroCardMetadata["heroPowerActivationTiming"] = command.HeroPowerActivationTiming;
      }

      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:play-hero-card",
        PlayHeroCardMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        heroCardPayload,
        "ON_PLAY",
        context.CommandSeq,
        null,
        $"play-card:{command.CardInstanceId}:0002",
        heroCardMetadata));

      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
        context.CommandSeq,
        null,
        "ON_PLAY",
        $"command:{context.Command.Kind}",
        "PLAY_CARD_ACTION_EXECUTOR",
        mutations,
        context.PreStateHash,
        null,
        null);
    }

    if (card.CardType is CardType.HeroPowerCard)
    {
      var heroPowerPayload = new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["heroPowerCost"] = command.HeroPowerCost!.Value
      };
      var heroPowerMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = command.CardInstanceId,
        ["heroPowerActivationTiming"] = command.HeroPowerActivationTiming,
        ["playerId"] = command.PlayerId
      };

      mutations.Add(new StateMutation(
        $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:replace-hero-power",
        ReplaceHeroPowerMutationHandler.SupportedMutationType,
        $"command:{context.Command.Kind}",
        command.PlayerId,
        heroPowerPayload,
        "ON_PLAY",
        context.CommandSeq,
        null,
        $"play-card:{command.CardInstanceId}:0002",
        heroPowerMetadata));

      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
        context.CommandSeq,
        null,
        "ON_PLAY",
        $"command:{context.Command.Kind}",
        "PLAY_CARD_ACTION_EXECUTOR",
        mutations,
        context.PreStateHash,
        null,
        null);
    }

    if (!isBoardEntityPlay)
    {
      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
        context.CommandSeq,
        null,
        "ON_PLAY",
        $"command:{context.Command.Kind}",
        "PLAY_CARD_ACTION_EXECUTOR",
        mutations,
        context.PreStateHash,
        null,
        null);
    }

    var summonMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["entityInstanceId"] = command.EntityInstanceId,
      ["entityType"] = "MINION",
      ["playerId"] = command.PlayerId,
      ["sourceCardInstanceId"] = command.CardInstanceId
    };
    if (command.MaxBoardSize.HasValue)
    {
      summonMetadata["maxBoardSize"] = command.MaxBoardSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    foreach (var keyword in (command.Keywords ?? []).Order(StringComparer.Ordinal).Select((value, index) => new { value, index }))
    {
      summonMetadata[$"entityKeyword:{keyword.index + 1:0000}"] = keyword.value;
    }

    mutations.Add(new StateMutation(
      $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:summon-entity",
      SummonEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      command.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["attack"] = command.Attack,
        ["health"] = command.Health
      },
      "ON_PLAY",
      context.CommandSeq,
      null,
      $"play-card:{command.CardInstanceId}:0002",
      summonMetadata));
    mutations.Add(new StateMutation(
      $"mutation:{context.CommandSeq}:play-card:{command.CardInstanceId}:mark-summoned-turn",
      IncrementCounterMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      CombatStateKeys.SummonCounterKey(command.PlayerId, command.EntityInstanceId),
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = context.State.TurnNumber
      },
      "ON_PLAY",
      context.CommandSeq,
      null,
      $"play-card:{command.CardInstanceId}:0003",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = command.EntityInstanceId,
        ["playerId"] = command.PlayerId
      }));

    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:play-card:{command.CardInstanceId}",
      context.CommandSeq,
      null,
      "ON_PLAY",
      $"command:{context.Command.Kind}",
      "PLAY_CARD_ACTION_EXECUTOR",
      mutations,
      context.PreStateHash,
      null,
      null);
  }

  private static StateMutation CreateDestroyEquippedEntityMutation(
    ActionExecutionContext context,
    string replacementCardInstanceId,
    EntityInstance entity,
    string reason,
    string orderingSuffix) =>
    new(
      $"mutation:{context.CommandSeq}:play-card:{replacementCardInstanceId}:{reason}:{entity.EntityInstanceId}",
      DestroyEquippedEntityMutationHandler.SupportedMutationType,
      $"command:{context.Command.Kind}",
      entity.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "ON_PLAY",
      context.CommandSeq,
      null,
      $"play-card:{replacementCardInstanceId}:{orderingSuffix}",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = entity.EntityInstanceId,
        ["entityType"] = ToEntityTypeId(entity.EntityType),
        ["playerId"] = entity.ControllerPlayerId,
        ["replacementCardInstanceId"] = replacementCardInstanceId,
        ["sourceCardInstanceId"] = entity.SourceCardInstanceId
      });

  private static string ToEntityTypeId(EntityType entityType) =>
    entityType switch
    {
      EntityType.Weapon => "WEAPON",
      EntityType.Location => "LOCATION",
      _ => entityType.ToString().ToUpperInvariant()
    };
}
