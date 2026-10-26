using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Primitives;

namespace Game.Application.Legality;

public sealed class LegalActionOracle : ILegalActionOracle
{
  private const string TauntKeyword = "TAUNT";
  private const string StealthKeyword = "STEALTH";

  public LegalActionResult GetLegalActions(LegalActionQuery query)
  {
    ArgumentNullException.ThrowIfNull(query);
    ArgumentNullException.ThrowIfNull(query.State);
    ArgumentNullException.ThrowIfNull(query.CardVersionsByVersionId);

    var diagnostics = new List<LegalActionDiagnostic>();
    var commands = new List<IGameCommand>();

    if (!query.State.Players.TryGetValue(query.PlayerId, out var player))
    {
      Add(diagnostics, "PLAYER_NOT_FOUND", "playerId", query.PlayerId, $"Player '{query.PlayerId}' does not exist.");
      return new LegalActionResult(commands, diagnostics);
    }

    AddConcedeCommandIfLegal(query, commands, diagnostics);

    if (!StringComparer.Ordinal.Equals(query.State.ActivePlayerId, query.PlayerId))
    {
      Add(diagnostics, "NOT_ACTIVE_PLAYER", "activePlayerId", query.State.ActivePlayerId ?? string.Empty, "Only the active player can issue priority gameplay commands.");
      return new LegalActionResult(commands, diagnostics);
    }

    if (!StringComparer.Ordinal.Equals(query.State.Phase, "TURN_START"))
    {
      Add(diagnostics, "INVALID_PLAY_PHASE", "phase", query.State.Phase, "Priority gameplay commands are available only during the active player's turn action phase.");
      return new LegalActionResult(commands, diagnostics);
    }

    AddEndTurnCommandIfLegal(query, commands, diagnostics);
    AddAttackCommandsIfLegal(query, player, commands, diagnostics);
    AddUseHeroPowerCommandsIfLegal(query, player, commands, diagnostics);
    AddActivateLocationCommandsIfLegal(query, player, commands, diagnostics);
    AddPlayCardCommandsIfLegal(query, player, commands, diagnostics);

    return new LegalActionResult(commands, diagnostics);
  }

  private static void AddPlayCardCommandsIfLegal(
    LegalActionQuery query,
    PlayerState player,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      PlayCardCommand.CommandKind,
      "UNSUPPORTED_PLAY_CARD_ACTION",
      diagnostics))
    {
      return;
    }

    foreach (var card in player.Zones.Hand.OrderBy(card => card.CardInstanceId, StringComparer.Ordinal))
    {
      AddLegalPlayCardCommand(query, player, card, commands, diagnostics);
    }
  }

  private static void AddLegalPlayCardCommand(
    LegalActionQuery query,
    PlayerState player,
    CardInstance card,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    var path = $"hand[{card.CardInstanceId}]";
    if (!StringComparer.Ordinal.Equals(card.OwnerPlayerId, player.PlayerId))
    {
      Add(diagnostics, "CARD_OWNER_MISMATCH", $"{path}.ownerPlayerId", card.OwnerPlayerId, "Only the owning player can play a card from hand.");
      return;
    }

    if (!query.CardVersionsByVersionId.TryGetValue(card.CardVersion, out var version))
    {
      Add(diagnostics, "CARD_VERSION_NOT_FOUND", $"{path}.cardVersion", card.CardVersion, "Card version is not present in the active catalog.");
      return;
    }

    if (!StringComparer.Ordinal.Equals(version.CardVersionId, card.CardVersion))
    {
      Add(diagnostics, "CARD_VERSION_MISMATCH", $"{path}.cardVersion", card.CardVersion, "Card instance version does not match the catalog card version.");
      return;
    }

    if (query.RulesetProfile is not null)
    {
      var cardType = CardTypeId(version.Definition.StaticData.CardType);
      if (!query.RulesetProfile.AllowedCardTypes.Contains(cardType, StringComparer.Ordinal))
      {
        Add(diagnostics, "RULESET_CARD_TYPE_DISABLED", $"{path}.cardType", cardType, "Ruleset profile does not allow this card type.");
        return;
      }

      if (!StringComparer.Ordinal.Equals(version.RulesetProfileHash, query.RulesetProfile.ResolvedHash))
      {
        Add(diagnostics, "RULESET_PROFILE_HASH_MISMATCH", $"{path}.rulesetProfileHash", version.RulesetProfileHash, "Card was not published for the active ruleset profile hash.");
        return;
      }
    }

    var baseManaCost = version.Definition.StaticData.ManaCost;
    var costReduction = CostModifierCalculator.CalculateReduction(
      player,
      baseManaCost,
      query.RulesetProfile?.Limits.MaxManaCostReduction);
    var effectiveManaCost = Math.Max(0, baseManaCost - costReduction);

    if (effectiveManaCost > player.Mana)
    {
      Add(diagnostics, "INSUFFICIENT_MANA", $"{path}.manaCost", effectiveManaCost.ToString(System.Globalization.CultureInfo.InvariantCulture), "Player does not have enough mana to play this card after cost modifiers.");
      return;
    }

    if (version.Definition.StaticData.CardType is CardType.SpellCard)
    {
      AddPlayCardTargetVariants(query,player,version,new PlayCardCommand(
        player.PlayerId,
        card.CardInstanceId,
        ManaCost: effectiveManaCost,
        CostReduction: costReduction),commands,diagnostics,allowUntargetedWhenNoCandidates:false);
      return;
    }

    if (version.Definition.StaticData.CardType is CardType.WeaponCard)
    {
      if (version.Definition.Entity.Weapon is null)
      {
        Add(diagnostics, "WEAPON_DEFINITION_MISSING", $"{path}.entity.weapon", card.CardVersion, "Weapon cards require a weapon definition.");
        return;
      }

      commands.Add(new PlayCardCommand(
        player.PlayerId,
        card.CardInstanceId,
        $"weapon:{card.CardInstanceId}",
        Attack: version.Definition.Entity.Weapon.Attack,
        ManaCost: effectiveManaCost,
        Keywords: version.Definition.Entity.Weapon.BaseKeywords,
        CostReduction: costReduction,
        Durability: version.Definition.Entity.Weapon.Durability));
      return;
    }

    if (version.Definition.StaticData.CardType is CardType.LocationCard)
    {
      if (version.Definition.Entity.Location is null)
      {
        Add(diagnostics, "LOCATION_DEFINITION_MISSING", $"{path}.entity.location", card.CardVersion, "Location cards require a location definition.");
        return;
      }

      commands.Add(new PlayCardCommand(
        player.PlayerId,
        card.CardInstanceId,
        $"location:{card.CardInstanceId}",
        ManaCost: effectiveManaCost,
        CostReduction: costReduction,
        Durability: version.Definition.Entity.Location.Durability));
      return;
    }

    if (version.Definition.StaticData.CardType is CardType.HeroCard)
    {
      if (version.Definition.Entity.HeroCard is null)
      {
        Add(diagnostics, "HERO_CARD_DEFINITION_MISSING", $"{path}.entity.heroCard", card.CardVersion, "Hero cards require a hero card definition.");
        return;
      }

      var heroCard = version.Definition.Entity.HeroCard;
      commands.Add(new PlayCardCommand(
        player.PlayerId,
        card.CardInstanceId,
        ManaCost: effectiveManaCost,
        CostReduction: costReduction,
        ArmorGain: heroCard.ArmorGain,
        HeroPowerCost: heroCard.HeroPowerReplacement?.Cost,
        HeroPowerActivationTiming: heroCard.HeroPowerReplacement?.ActivationTiming ?? string.Empty));
      return;
    }

    if (version.Definition.StaticData.CardType is CardType.HeroPowerCard)
    {
      if (version.Definition.Entity.HeroPower is null)
      {
        Add(diagnostics, "HERO_POWER_DEFINITION_MISSING", $"{path}.entity.heroPower", card.CardVersion, "Hero power cards require a hero power definition.");
        return;
      }

      var heroPower = version.Definition.Entity.HeroPower;
      commands.Add(new PlayCardCommand(
        player.PlayerId,
        card.CardInstanceId,
        ManaCost: effectiveManaCost,
        CostReduction: costReduction,
        HeroPowerCost: heroPower.Cost,
        HeroPowerActivationTiming: heroPower.ActivationTiming));
      return;
    }

    if (version.Definition.StaticData.CardType is not (CardType.MinionCard or CardType.TokenCard)
      || version.Definition.Entity.Minion is null)
    {
      Add(diagnostics, "UNSUPPORTED_CARD_PLAY_PATH", $"{path}.cardType", CardTypeId(version.Definition.StaticData.CardType), "Only minion, token, spell, weapon, location, and hero-card PLAY_CARD materialization is implemented in this slice.");
      return;
    }

    if (query.RulesetProfile is not null
      && query.RulesetProfile.Limits.MaxBoardSummonsPerAction < 1)
    {
      Add(
        diagnostics,
        "RULESET_BOARD_SUMMON_LIMIT_REACHED",
        "rulesetProfile.limits.maxBoardSummonsPerAction",
        query.RulesetProfile.Limits.MaxBoardSummonsPerAction.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Ruleset profile does not allow this PLAY_CARD path to summon a board entity.");
      return;
    }

    if (query.RulesetProfile is not null
      && player.Zones.Board.Count >= query.RulesetProfile.Limits.MaxBoardSize)
    {
      Add(
        diagnostics,
        "RULESET_BOARD_CAPACITY_REACHED",
        "rulesetProfile.limits.maxBoardSize",
        query.RulesetProfile.Limits.MaxBoardSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Ruleset profile board capacity prevents this PLAY_CARD path from summoning another board entity.");
      return;
    }

    var entityInstanceId = $"entity:{card.CardInstanceId}";
    if (player.Zones.ContainsEntityOnBoard(entityInstanceId))
    {
      Add(diagnostics, "ENTITY_INSTANCE_ALREADY_EXISTS", $"{path}.entityInstanceId", entityInstanceId, "Generated entity instance id already exists on board.");
      return;
    }

    AddPlayCardTargetVariants(query,player,version,new PlayCardCommand(
      player.PlayerId,
      card.CardInstanceId,
      entityInstanceId,
      version.Definition.Entity.Minion.Attack,
      version.Definition.Entity.Minion.Health,
      effectiveManaCost,
      query.RulesetProfile?.Limits.MaxBoardSize,
      version.Definition.Entity.Minion.BaseKeywords,
      costReduction),commands,diagnostics,allowUntargetedWhenNoCandidates:true);
  }

  private static void AddPlayCardTargetVariants(LegalActionQuery query,PlayerState player,CardVersion version,PlayCardCommand command,ICollection<IGameCommand> commands,ICollection<LegalActionDiagnostic> diagnostics,bool allowUntargetedWhenNoCandidates)
  {
    var targetSpec=version.Definition.Effects.Where(effect=>effect.SourceZones.Contains(SourceZone.Hand)&&effect.TimingPolicy.Phase is Phase.OnPlay).FirstOrDefault(effect=>effect.TargetRequirement is not null)?.TargetRequirement;
    if(targetSpec is null||targetSpec.SelectionMode is TargetSelectionMode.None){commands.Add(command);return;}
    if(targetSpec.SelectionMode is not TargetSelectionMode.PlayerChosen||targetSpec.Count.Min>1||targetSpec.Count.Max!=1){Add(diagnostics,"PLAY_CARD_TARGETING_UNSUPPORTED","playCard.targetRequirement",targetSpec.SelectionMode.ToString(),"Only optional or one-target player-chosen play effects are supported.");return;}
    var targets=EnumerateHeroPowerTargets(query.State,player.PlayerId,targetSpec);
    foreach(var target in targets)commands.Add(command with{TargetPlayerId=target.PlayerId,TargetEntityInstanceId=target.EntityInstanceId});
    if(targets.Count==0&&allowUntargetedWhenNoCandidates)commands.Add(command);
    else if(targets.Count==0&&targetSpec.Required)Add(diagnostics,"PLAY_CARD_HAS_NO_VALID_TARGET","playCard.targetRequirement",command.CardInstanceId,"The targeted play effect has no valid target.");
    else if(targets.Count==0)commands.Add(command);
  }

  private static void AddAttackCommandsIfLegal(
    LegalActionQuery query,
    PlayerState player,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      AttackCommand.CommandKind,
      "UNSUPPORTED_ATTACK_ACTION",
      diagnostics))
    {
      return;
    }

    var targets = query.State.Players.Values
      .Where(candidate => !StringComparer.Ordinal.Equals(candidate.PlayerId, player.PlayerId))
      .OrderBy(candidate => candidate.PlayerId, StringComparer.Ordinal)
      .SelectMany(candidate =>
      {
        var hasTargetableTaunt = candidate.Zones.Board.Any(entity =>
          IsAlive(entity)
          && HasKeyword(entity, TauntKeyword)
          && !HasKeyword(entity, StealthKeyword));
        var boardTargets = candidate.Zones.Board
          .Where(IsAlive)
          .Where(entity => !HasKeyword(entity, StealthKeyword))
          .Where(entity => !hasTargetableTaunt || HasKeyword(entity, TauntKeyword))
          .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
          .Select(entity => new AttackTarget(candidate.PlayerId, entity.EntityInstanceId));
        var heroTarget = !hasTargetableTaunt && candidate.Hero is not null && IsAlive(candidate.Hero)
          ? new[] { new AttackTarget(candidate.PlayerId, candidate.Hero.EntityInstanceId) }
          : Array.Empty<AttackTarget>();
        return boardTargets.Concat(heroTarget);
      })
      .ToArray();

    foreach (var attacker in player.Zones.Board.OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal))
    {
      var path = $"board[{attacker.EntityInstanceId}]";
      if (!IsAlive(attacker))
      {
        Add(diagnostics, "ATTACKER_NOT_ALIVE", $"{path}.damage", attacker.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), "Only living minions can attack.");
        continue;
      }

      if (attacker.Attack <= 0)
      {
        Add(diagnostics, "ATTACKER_HAS_NO_ATTACK", $"{path}.attack", attacker.Attack.ToString(System.Globalization.CultureInfo.InvariantCulture), "Only minions with attack greater than zero can attack.");
        continue;
      }

      if (query.State.Counters.TryGetValue(
        CombatStateKeys.SummonCounterKey(player.PlayerId, attacker.EntityInstanceId),
        out var summonedTurn)
        && summonedTurn == query.State.TurnNumber)
      {
        Add(diagnostics, "SUMMON_SICKNESS", path, attacker.EntityInstanceId, "A minion cannot attack on the same turn it was summoned.");
        continue;
      }

      if (query.State.Counters.TryGetValue(
        CombatStateKeys.AttackCounterKey(query.State.TurnNumber, player.PlayerId, attacker.EntityInstanceId),
        out var attacksThisTurn)
        && attacksThisTurn > 0)
      {
        Add(diagnostics, "ATTACK_ALREADY_USED", path, attacker.EntityInstanceId, "A minion can attack only once per turn in the current combat slice.");
        continue;
      }

      foreach (var target in targets)
      {
        commands.Add(new AttackCommand(
          player.PlayerId,
          attacker.EntityInstanceId,
          target.PlayerId,
          target.EntityInstanceId));
      }
    }

    if (player.Weapon is null)
    {
      return;
    }

    var weaponPath = $"weapon[{player.Weapon.EntityInstanceId}]";
    if (!IsAlive(player.Weapon))
    {
      Add(diagnostics, "WEAPON_NOT_USABLE", $"{weaponPath}.damage", player.Weapon.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), "Only a weapon with remaining durability can attack.");
      return;
    }

    if (player.Weapon.Attack <= 0)
    {
      Add(diagnostics, "WEAPON_HAS_NO_ATTACK", $"{weaponPath}.attack", player.Weapon.Attack.ToString(System.Globalization.CultureInfo.InvariantCulture), "Only weapons with attack greater than zero can attack.");
      return;
    }

    if (query.State.Counters.TryGetValue(
      CombatStateKeys.AttackCounterKey(query.State.TurnNumber, player.PlayerId, player.Weapon.EntityInstanceId),
      out var weaponAttacksThisTurn)
      && weaponAttacksThisTurn > 0)
    {
      Add(diagnostics, "WEAPON_ATTACK_ALREADY_USED", weaponPath, player.Weapon.EntityInstanceId, "A weapon can attack only once per turn in the current combat slice.");
      return;
    }

    foreach (var target in targets)
    {
      commands.Add(new AttackCommand(
        player.PlayerId,
        player.Weapon.EntityInstanceId,
        target.PlayerId,
        target.EntityInstanceId));
    }
  }

  private static void AddConcedeCommandIfLegal(
    LegalActionQuery query,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      ConcedeCommand.CommandKind,
      "UNSUPPORTED_CONCEDE_ACTION",
      diagnostics))
    {
      return;
    }

    if (query.State.Players.Keys.Any(playerId => !StringComparer.Ordinal.Equals(playerId, query.PlayerId)))
    {
      commands.Add(new ConcedeCommand(query.PlayerId));
    }
  }

  private static void AddUseHeroPowerCommandsIfLegal(
    LegalActionQuery query,
    PlayerState player,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      UseHeroPowerCommand.CommandKind,
      "UNSUPPORTED_USE_HERO_POWER_ACTION",
      diagnostics))
    {
      return;
    }

    if (player.HeroPower is null)
    {
      Add(diagnostics, "HERO_POWER_MISSING", "heroPower", player.PlayerId, "Player does not have an active hero power.");
      return;
    }

    if (player.Mana < player.HeroPower.Cost)
    {
      Add(diagnostics, "INSUFFICIENT_MANA", "heroPower.cost", player.HeroPower.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture), "Player does not have enough mana to use the active hero power.");
      return;
    }

    if (query.State.Counters.TryGetValue(
      UseHeroPowerActionExecutor.ActivationCounterKey(
        query.State.TurnNumber,
        player.PlayerId,
        player.HeroPower.SourceCardInstanceId),
      out var usedCount)
      && usedCount > 0)
    {
      Add(diagnostics, "HERO_POWER_ALREADY_USED", "heroPower", player.HeroPower.SourceCardInstanceId, "A hero power can be used only once per turn in the current runtime slice.");
      return;
    }

    var sourceCard = player.Zones.SetAside
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Deck)
      .Concat(player.Zones.Graveyard)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, player.HeroPower.SourceCardInstanceId));
    if (sourceCard is null)
    {
      Add(diagnostics, "HERO_POWER_SOURCE_CARD_NOT_FOUND", "heroPower.sourceCardInstanceId", player.HeroPower.SourceCardInstanceId, "Hero power source card is not present in a player zone.");
      return;
    }

    if (!query.CardVersionsByVersionId.TryGetValue(sourceCard.CardVersion, out var version))
    {
      Add(diagnostics, "HERO_POWER_CARD_VERSION_NOT_FOUND", "heroPower.sourceCardVersion", sourceCard.CardVersion, "Hero power source card version is not present in the active catalog.");
      return;
    }

    var heroPower = version.Definition.Entity.HeroCard?.HeroPowerReplacement
      ?? version.Definition.Entity.HeroPower;
    if (heroPower is null)
    {
      Add(diagnostics, "HERO_POWER_DEFINITION_MISSING", "heroPower.sourceCardVersion", sourceCard.CardVersion, "Hero power source card does not define a hero power.");
      return;
    }

    if (heroPower.Cost != player.HeroPower.Cost
      || !StringComparer.Ordinal.Equals(heroPower.ActivationTiming, player.HeroPower.ActivationTiming))
    {
      Add(diagnostics, "HERO_POWER_STATE_MISMATCH", "heroPower", player.HeroPower.SourceCardInstanceId, "Hero power state does not match the active catalog definition.");
      return;
    }

    if (!StringComparer.Ordinal.Equals(heroPower.ActivationTiming, "MAIN_PHASE"))
    {
      Add(diagnostics, "HERO_POWER_TIMING_UNSUPPORTED", "heroPower.activationTiming", heroPower.ActivationTiming, "Only MAIN_PHASE hero power activation is supported in the current runtime slice.");
      return;
    }

    var targetSpec = heroPower.TargetRequirement
      ?? heroPower.Effects.FirstOrDefault(effect => effect.TargetRequirement is not null)?.TargetRequirement;
    if (targetSpec is null || targetSpec.SelectionMode is TargetSelectionMode.None)
    {
      commands.Add(new UseHeroPowerCommand(
        player.PlayerId,
        player.HeroPower.SourceCardInstanceId,
        player.HeroPower.Cost));
      return;
    }

    if (targetSpec.SelectionMode is not TargetSelectionMode.PlayerChosen
      || targetSpec.Count.Min > 1
      || targetSpec.Count.Max != 1)
    {
      Add(diagnostics, "HERO_POWER_TARGETING_UNSUPPORTED", "heroPower.targetRequirement.selectionMode", targetSpec.SelectionMode.ToString(), "Only optional or one-target player-chosen hero powers are supported in the current runtime slice.");
      return;
    }

    foreach (var target in EnumerateHeroPowerTargets(query.State, player.PlayerId, targetSpec))
    {
      commands.Add(new UseHeroPowerCommand(
        player.PlayerId,
        player.HeroPower.SourceCardInstanceId,
        player.HeroPower.Cost,
        target.PlayerId,
        target.EntityInstanceId));
    }
  }

  private static void AddActivateLocationCommandsIfLegal(
    LegalActionQuery query,
    PlayerState player,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      ActivateLocationCommand.CommandKind,
      "UNSUPPORTED_ACTIVATE_LOCATION_ACTION",
      diagnostics))
    {
      return;
    }

    if (player.Location is null)
    {
      Add(diagnostics, "LOCATION_MISSING", "location", player.PlayerId, "Player does not have an active location.");
      return;
    }

    if (!IsAlive(player.Location))
    {
      Add(diagnostics, "LOCATION_NOT_USABLE", "location.damage", player.Location.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), "Location cannot be activated with no remaining durability.");
      return;
    }

    if (query.State.Counters.TryGetValue(
      ActivateLocationActionExecutor.ActivationCounterKey(
        query.State.TurnNumber,
        player.PlayerId,
        player.Location.EntityInstanceId),
      out var usedCount)
      && usedCount > 0)
    {
      Add(diagnostics, "LOCATION_ALREADY_USED", "location", player.Location.EntityInstanceId, "A location can be activated only once per turn in the current runtime slice.");
      return;
    }

    var sourceCard = player.Zones.SetAside
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Deck)
      .Concat(player.Zones.Graveyard)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, player.Location.SourceCardInstanceId));
    if (sourceCard is null)
    {
      Add(diagnostics, "LOCATION_SOURCE_CARD_NOT_FOUND", "location.sourceCardInstanceId", player.Location.SourceCardInstanceId, "Location source card is not present in a player zone.");
      return;
    }

    if (!query.CardVersionsByVersionId.TryGetValue(sourceCard.CardVersion, out var version))
    {
      Add(diagnostics, "LOCATION_CARD_VERSION_NOT_FOUND", "location.sourceCardVersion", sourceCard.CardVersion, "Location source card version is not present in the active catalog.");
      return;
    }

    if (version.Definition.Entity.Location is null)
    {
      Add(diagnostics, "LOCATION_DEFINITION_MISSING", "location.sourceCardVersion", sourceCard.CardVersion, "Location source card does not define a location.");
      return;
    }

    if (!StringComparer.Ordinal.Equals(version.Definition.Entity.Location.ActivationLimit, "ONCE_PER_TURN"))
    {
      Add(diagnostics, "LOCATION_ACTIVATION_LIMIT_UNSUPPORTED", "location.activationLimit", version.Definition.Entity.Location.ActivationLimit, "Only ONCE_PER_TURN location activation is supported in the current runtime slice.");
      return;
    }

    var targetSpec = version.Definition.Effects
      .Where(effect => effect.EffectKind is EffectKind.LocationAbilityEffect)
      .FirstOrDefault(effect => effect.TargetRequirement is not null)
      ?.TargetRequirement;
    if (targetSpec is null || targetSpec.SelectionMode is TargetSelectionMode.None)
    {
      commands.Add(new ActivateLocationCommand(
        player.PlayerId,
        player.Location.EntityInstanceId,
        player.Location.SourceCardInstanceId));
      return;
    }

    if (targetSpec.SelectionMode is not TargetSelectionMode.PlayerChosen
      || targetSpec.Count.Min > 1
      || targetSpec.Count.Max != 1)
    {
      Add(diagnostics, "LOCATION_TARGETING_UNSUPPORTED", "location.targetRequirement.selectionMode", targetSpec.SelectionMode.ToString(), "Only optional or one-target player-chosen locations are supported in the current runtime slice.");
      return;
    }

    foreach (var target in EnumerateHeroPowerTargets(query.State, player.PlayerId, targetSpec))
    {
      commands.Add(new ActivateLocationCommand(
        player.PlayerId,
        player.Location.EntityInstanceId,
        player.Location.SourceCardInstanceId,
        target.PlayerId,
        target.EntityInstanceId));
    }
  }

  private static IReadOnlyList<HeroPowerTarget> EnumerateHeroPowerTargets(
    IGameState state,
    string playerId,
    Game.Shared.Dsl.TargetSpec targetSpec)
  {
    var query = targetSpec.Candidates;
    if (query.RequiredTags.Count > 0 || query.ExcludedTags.Count > 0)
    {
      return [];
    }

    var controllerPlayerIds = query.Controller.Trim().ToUpperInvariant() switch
    {
      "SELF" or "SOURCE_CONTROLLER" => [playerId],
      "OPPONENT" => state.Players.Keys
        .Where(candidate => !StringComparer.Ordinal.Equals(candidate, playerId))
        .Order(StringComparer.Ordinal)
        .ToArray(),
      "ANY" or "ALL" => state.Players.Keys.Order(StringComparer.Ordinal).ToArray(),
      _ => []
    };
    var allowedTypes = query.EntityTypes.Count == 0
      ? null
      : query.EntityTypes.ToHashSet();
    var targets = new List<HeroPowerTarget>();
    foreach (var controllerPlayerId in controllerPlayerIds)
    {
      if (!state.Players.TryGetValue(controllerPlayerId, out var controller))
      {
        continue;
      }

      if (query.Zones.Contains(SourceZone.Board))
      {
        targets.AddRange(controller.Zones.Board
          .Where(entity => HeroPowerTargetMatches(entity, allowedTypes, query.AliveOnly))
          .Select(entity => new HeroPowerTarget(controllerPlayerId, entity.EntityInstanceId)));
      }

      if (query.Zones.Contains(SourceZone.Global)
        && controller.Hero is not null
        && HeroPowerTargetMatches(controller.Hero, allowedTypes, query.AliveOnly))
      {
        targets.Add(new HeroPowerTarget(controllerPlayerId, controller.Hero.EntityInstanceId));
      }
    }

    return targets
      .OrderBy(target => target.PlayerId, StringComparer.Ordinal)
      .ThenBy(target => target.EntityInstanceId, StringComparer.Ordinal)
      .ToArray();
  }

  private static bool HeroPowerTargetMatches(
    EntityInstance entity,
    IReadOnlySet<EntityType>? allowedTypes,
    bool aliveOnly)
  {
    if (allowedTypes is not null && !allowedTypes.Contains(entity.EntityType))
    {
      return false;
    }

    return !aliveOnly || entity.Damage < entity.Health;
  }

  private static void AddEndTurnCommandIfLegal(
    LegalActionQuery query,
    ICollection<IGameCommand> commands,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (!IsActionEnabled(
      query,
      EndTurnCommand.CommandKind,
      "UNSUPPORTED_END_TURN_ACTION",
      diagnostics))
    {
      return;
    }

    commands.Add(new EndTurnCommand(
      query.PlayerId,
      query.RulesetProfile?.Limits.MaxHandSize));
  }

  private static bool IsActionEnabled(
    LegalActionQuery query,
    string actionType,
    string unsupportedCode,
    ICollection<LegalActionDiagnostic> diagnostics)
  {
    if (query.EngineCapabilities is not null
      && !query.EngineCapabilities.SupportedActionTypes.Contains(actionType, StringComparer.Ordinal))
    {
      Add(diagnostics, unsupportedCode, "engineCapabilities.supportedActionTypes", actionType, $"Engine capabilities do not enable {actionType}.");
      return false;
    }

    if (query.RulesetProfile is not null
      && !query.RulesetProfile.AllowedActions.Contains(actionType, StringComparer.Ordinal))
    {
      Add(diagnostics, "RULESET_ACTION_DISABLED", "rulesetProfile.allowedActions", actionType, $"Ruleset profile does not allow {actionType}.");
      return false;
    }

    return true;
  }

  private static string CardTypeId(CardType cardType) =>
    cardType switch
    {
      CardType.MinionCard => "MINION_CARD",
      CardType.SpellCard => "SPELL_CARD",
      CardType.WeaponCard => "WEAPON_CARD",
      CardType.LocationCard => "LOCATION_CARD",
      CardType.HeroCard => "HERO_CARD",
      CardType.HeroPowerCard => "HERO_POWER_CARD",
      CardType.TokenCard => "TOKEN_CARD",
      _ => cardType.ToString()
    };

  private static bool IsAlive(EntityInstance entity) => entity.Health - entity.Damage > 0;

  private static bool HasKeyword(EntityInstance entity, string keyword) =>
    (entity.Keywords ?? []).Contains(keyword, StringComparer.Ordinal);

  private static void Add(
    ICollection<LegalActionDiagnostic> diagnostics,
    string code,
    string path,
    string identifier,
    string message)
  {
    diagnostics.Add(new LegalActionDiagnostic(code, path, identifier, message));
  }

  private readonly record struct AttackTarget(
    string PlayerId,
    string EntityInstanceId);

  private readonly record struct HeroPowerTarget(
    string PlayerId,
    string EntityInstanceId);
}
