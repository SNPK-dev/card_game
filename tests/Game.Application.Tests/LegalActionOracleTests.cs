using Game.Application.Capabilities;
using Game.Application.Legality;
using Game.Application.Simulation;
using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Randomness;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Engine;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class LegalActionOracleTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalMinionInHand()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 2);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Equal("entity:card-instance-1", command.EntityInstanceId);
    Assert.Equal(2, command.Attack);
    Assert.Equal(3, command.Health);
    Assert.Equal(2, command.ManaCost);
    Assert.Null(command.Keywords);
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
    Assert.Contains(result.Commands, command => command is ConcedeCommand);
  }

  [Fact]
  public void OracleAllowsTargetedBattlecryMinionWhenBoardHasNoLegalTarget()
  {
    var card = CreateCardInstance("nixara-instance");
    var target = new TargetSpec(true, TargetSelectionMode.PlayerChosen,
      new EntityQuery("ANY", [SourceZone.Board], [EntityType.Minion], [], [], true),
      new CountSpec(1, 1), "FIZZLE");
    var effect = new EffectDefinition("nixara-battlecry", EffectKind.SimplePlayEffect,
      [SourceZone.Hand], null, new TimingPolicy(Phase.OnPlay, null, 0, "CARD_TEXT_ORDER"),
      [], target, [], new ResolutionPolicy("SOURCE_ORDER"), new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
      new ReplayPolicy(true, false), []);
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, effects: [effect]);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(state, "player-1", Versions(version)));

    var command = Assert.Single(result.Commands.OfType<PlayCardCommand>());
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Empty(command.TargetPlayerId);
    Assert.Empty(command.TargetEntityInstanceId);
    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "PLAY_CARD_HAS_NO_VALID_TARGET");
  }

  [Fact]
  public void OracleReturnsPlayCardCommandWithMinionBaseKeywords()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, baseKeywords: ["TAUNT"]);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(["TAUNT"], command.Keywords);
  }

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalSpellInHand()
  {
    var card = CreateCardInstance("spell-instance-1", CardType.SpellCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, cardType: CardType.SpellCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var profile = CreateProfile(allowedCardTypes: ["SPELL_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Empty(command.EntityInstanceId);
    Assert.Equal(0, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(2, command.ManaCost);
    Assert.Null(command.MaxBoardSize);
    Assert.Null(command.Keywords);
  }

  [Fact]
  public void OracleAppliesCostModifiersToSpellPlayCardCost()
  {
    var card = CreateCardInstance("spell-instance-1", CardType.SpellCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 3, cardType: CardType.SpellCard);
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);
    var profile = CreateProfile(allowedCardTypes: ["SPELL_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(1, command.ManaCost);
    Assert.Equal(2, command.CostReduction);
  }

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalWeaponInHand()
  {
    var card = CreateCardInstance("weapon-instance-1", CardType.WeaponCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 4, cardType: CardType.WeaponCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 4);
    var profile = CreateProfile(allowedCardTypes: ["WEAPON_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Equal("weapon:weapon-instance-1", command.EntityInstanceId);
    Assert.Equal(3, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(2, command.Durability);
    Assert.Equal(4, command.ManaCost);
    Assert.Equal(["LIFESTEAL"], command.Keywords);
  }

  [Fact]
  public void OracleReturnsWeaponPlayWhenWeaponSlotIsOccupied()
  {
    var card = CreateCardInstance("weapon-instance-1", CardType.WeaponCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 0, cardType: CardType.WeaponCard);
    var weapon = CreateEntity("weapon-existing", "player-1", attack: 1, health: 2, entityType: EntityType.Weapon);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10, weapon: weapon);
    var profile = CreateProfile(allowedCardTypes: ["WEAPON_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Equal("weapon:weapon-instance-1", command.EntityInstanceId);
    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "WEAPON_SLOT_OCCUPIED");
  }

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalLocationInHand()
  {
    var card = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, cardType: CardType.LocationCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var profile = CreateProfile(allowedCardTypes: ["LOCATION_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Equal("location:location-instance-1", command.EntityInstanceId);
    Assert.Equal(0, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(3, command.Durability);
    Assert.Equal(2, command.ManaCost);
    Assert.Null(command.Keywords);
  }

  [Fact]
  public void OracleReturnsLocationPlayWhenLocationSlotIsOccupied()
  {
    var card = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 0, cardType: CardType.LocationCard);
    var location = CreateEntity("location-existing", "player-1", attack: 0, health: 3, entityType: EntityType.Location);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10, location: location);
    var profile = CreateProfile(allowedCardTypes: ["LOCATION_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Equal("location:location-instance-1", command.EntityInstanceId);
    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "LOCATION_SLOT_OCCUPIED");
  }

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalHeroCardInHand()
  {
    var card = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 7);
    var profile = CreateProfile(allowedCardTypes: ["HERO_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Empty(command.EntityInstanceId);
    Assert.Equal(5, command.ArmorGain);
    Assert.Equal(2, command.HeroPowerCost);
    Assert.Equal("MAIN_PHASE", command.HeroPowerActivationTiming);
    Assert.Equal(7, command.ManaCost);
  }

  [Fact]
  public void OracleReturnsPlayCardCommandForLegalHeroPowerCardInHand()
  {
    var card = CreateCardInstance("hero-power-card-instance-1", CardType.HeroPowerCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 1, cardType: CardType.HeroPowerCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 1);
    var profile = CreateProfile(allowedCardTypes: ["HERO_POWER_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(card.CardInstanceId, command.CardInstanceId);
    Assert.Empty(command.EntityInstanceId);
    Assert.Equal(0, command.ArmorGain);
    Assert.Equal(1, command.HeroPowerCost);
    Assert.Equal("MAIN_PHASE", command.HeroPowerActivationTiming);
    Assert.Equal(1, command.ManaCost);
  }

  [Fact]
  public void OracleReturnsUseHeroPowerCommandForActiveHeroPower()
  {
    var sourceCard = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"));

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    var command = Assert.IsType<UseHeroPowerCommand>(Assert.Single(result.Commands.OfType<UseHeroPowerCommand>()));
    Assert.Equal(sourceCard.CardInstanceId, command.SourceCardInstanceId);
    Assert.Equal(2, command.ManaCost);
  }

  [Fact]
  public void OracleRejectsUseHeroPowerWhenAlreadyUsedThisTurn()
  {
    var sourceCard = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"),
      counters: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        [UseHeroPowerActionExecutor.ActivationCounterKey(1, "player-1", sourceCard.CardInstanceId)] = 1
      });

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    Assert.DoesNotContain(result.Commands, command => command is UseHeroPowerCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "HERO_POWER_ALREADY_USED");
  }



  [Fact]
  public void OracleRejectsPlayWhenPlayerIsNotActive()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), activePlayerId: "player-2");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    Assert.Contains(result.Commands, command => command is ConcedeCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "NOT_ACTIVE_PLAYER");
  }

  [Fact]
  public void OracleRejectsPlayWhenCardIsNotInActiveCatalog()
  {
    var card = CreateCardInstance("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CARD_VERSION_NOT_FOUND");
  }

  [Fact]
  public void OracleRejectsPlayWhenManaIsInsufficient()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "INSUFFICIENT_MANA");
  }

  [Fact]
  public void OracleAppliesNextPlayCostModifierToPlayCardCost()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version)));

    var command = Assert.IsType<PlayCardCommand>(Assert.Single(result.Commands.OfType<PlayCardCommand>()));
    Assert.Equal(1, command.ManaCost);
    Assert.Equal(2, command.CostReduction);
  }

  [Fact]
  public void OracleCapsCostModifierReductionByRulesetLimit()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 3)]);
    var profile = CreateProfile(
      allowedCardTypes: ["MINION_CARD"],
      limits: CreateLimits(maxManaCostReduction: 1));

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "INSUFFICIENT_MANA");
    Assert.Equal("2", diagnostic.Identifier);
  }

  [Fact]
  public void OracleAppliesRulesetCardTypeAndProfileHashGates()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);
    var profile = CreateProfile(allowedCardTypes: ["SPELL_CARD"]);

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RULESET_CARD_TYPE_DISABLED");
  }

  [Fact]
  public void OracleRejectsPlayWhenRulesetDisallowsBoardSummons()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);
    var profile = CreateProfile(
      allowedCardTypes: ["MINION_CARD"],
      limits: CreateLimits(maxBoardSummonsPerAction: 0));

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "RULESET_BOARD_SUMMON_LIMIT_REACHED");
    Assert.Equal("rulesetProfile.limits.maxBoardSummonsPerAction", diagnostic.Path);
    Assert.Equal("0", diagnostic.Identifier);
  }

  [Fact]
  public void OracleRejectsPlayWhenRulesetBoardCapacityIsFull()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var existing = CreateEntity("entity-existing", "player-1", attack: 1, health: 1);
    var state = CreateState(new PlayerZones(hand: [card], board: [existing]), mana: 10);
    var profile = CreateProfile(
      allowedCardTypes: ["MINION_CARD"],
      limits: CreateLimits(maxBoardSize: 1));

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      profile));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "RULESET_BOARD_CAPACITY_REACHED");
    Assert.Equal("rulesetProfile.limits.maxBoardSize", diagnostic.Path);
    Assert.Equal("1", diagnostic.Identifier);
  }

  [Fact]
  public void OracleAppliesEngineCapabilityActionGate()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);
    var registry = CreateRegistryWithoutPlayCard();

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      Versions(version),
      EngineCapabilities: registry));

    Assert.DoesNotContain(result.Commands, command => command is PlayCardCommand);
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UNSUPPORTED_PLAY_CARD_ACTION");
  }

  [Fact]
  public void OracleReturnsAttackAndEndTurnCommandsForReadyBoardMinion()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var opponentMinion = CreateEntity("entity-defender", "player-2", attack: 1, health: 2);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = opponentHero,
          Zones = new PlayerZones(board: [opponentMinion])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    var attacks = result.Commands.OfType<AttackCommand>().ToArray();
    Assert.Equal(2, attacks.Length);
    Assert.Contains(attacks, command => command.TargetEntityInstanceId == opponentMinion.EntityInstanceId);
    Assert.Contains(attacks, command => command.TargetEntityInstanceId == opponentHero.EntityInstanceId);
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
    Assert.Contains(result.Commands, command => command is ConcedeCommand);
  }

  [Fact]
  public void OracleReturnsWeaponAttackAgainstEnemyHero()
  {
    var weapon = CreateEntity("weapon-player-1", "player-1", attack: 3, health: 2, entityType: EntityType.Weapon);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with { Weapon = weapon },
        ["player-2"] = PlayerState.Empty("player-2") with { Hero = opponentHero }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    var attack = Assert.Single(result.Commands.OfType<AttackCommand>());
    Assert.Equal(weapon.EntityInstanceId, attack.AttackerEntityInstanceId);
    Assert.Equal("player-2", attack.TargetPlayerId);
    Assert.Equal(opponentHero.EntityInstanceId, attack.TargetEntityInstanceId);
  }

  [Fact]
  public void OracleRequiresTargetingTauntMinionsBeforeOtherTargets()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var taunt = CreateEntity("entity-taunt", "player-2", attack: 1, health: 4, keywords: ["TAUNT"]);
    var other = CreateEntity("entity-other", "player-2", attack: 1, health: 2);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = opponentHero,
          Zones = new PlayerZones(board: [other, taunt])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    var attack = Assert.Single(result.Commands.OfType<AttackCommand>());
    Assert.Equal(taunt.EntityInstanceId, attack.TargetEntityInstanceId);
  }

  [Fact]
  public void OracleExcludesStealthedTargets()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var stealthed = CreateEntity("entity-stealth", "player-2", attack: 1, health: 2, keywords: ["STEALTH"]);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = opponentHero,
          Zones = new PlayerZones(board: [stealthed])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    var attack = Assert.Single(result.Commands.OfType<AttackCommand>());
    Assert.Equal(opponentHero.EntityInstanceId, attack.TargetEntityInstanceId);
  }

  [Fact]
  public void OracleRejectsAttackWhenSummonSicknessCounterMatchesCurrentTurn()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var state = new InMemoryGameState(
      counters: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        [CombatStateKeys.SummonCounterKey("player-1", attacker.EntityInstanceId)] = 2
      },
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = opponentHero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");

    var result = new LegalActionOracle().GetLegalActions(new LegalActionQuery(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.DoesNotContain(result.Commands, command => command is AttackCommand);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SUMMON_SICKNESS");
    Assert.Contains(result.Commands, command => command is EndTurnCommand);
  }

  [Fact]
  public void OraclePriorityBotUsesOracleAndEndsTurnWhenNoLegalPlayExists()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 0);
    var bot = new OraclePriorityBot();

    var decision = bot.Decide(new BotDecisionContext(
      state,
      "player-1",
      Versions(version)));

    Assert.IsType<EndTurnCommand>(decision.Command);
  }

  [Fact]
  public void OraclePriorityBotChoosesHeroAttackFromOracleWhenNoPlayExists()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var opponentHero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var opponentMinion = CreateEntity("entity-defender", "player-2", attack: 1, health: 2);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = opponentHero,
          Zones = new PlayerZones(board: [opponentMinion])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");
    var bot = new OraclePriorityBot();

    var decision = bot.Decide(new BotDecisionContext(
      state,
      "player-1",
      new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    var attack = Assert.IsType<AttackCommand>(decision.Command);
    Assert.Equal(opponentHero.EntityInstanceId, attack.TargetEntityInstanceId);
  }

  [Fact]
  public void OraclePriorityBotUsesSeparateBotRngForSamePriorityChoices()
  {
    var left = CreateCardInstance("card-instance-a");
    var right = CreateCardInstance("card-instance-b");
    var leftVersion = CreateCardVersion(left.CardVersion, manaCost: 0);
    var rightVersion = CreateCardVersion(right.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [left, right]), mana: 10);
    var bot = new OraclePriorityBot();

    var first = bot.Decide(new BotDecisionContext(
      state,
      "player-1",
      Versions(leftVersion, rightVersion),
      CommandSeq: 1,
      BotRngRoller: new DeterministicRngRoller(new BotRandomStream(1))));
    var second = bot.Decide(new BotDecisionContext(
      state,
      "player-1",
      Versions(leftVersion, rightVersion),
      CommandSeq: 1,
      BotRngRoller: new DeterministicRngRoller(new BotRandomStream(4))));

    var firstPlay = Assert.IsType<PlayCardCommand>(first.Command);
    var secondPlay = Assert.IsType<PlayCardCommand>(second.Command);
    Assert.NotNull(first.RngRoll);
    Assert.NotNull(second.RngRoll);
    Assert.Equal(RngStreamKind.BotDecision, first.RngRoll.Stream);
    Assert.Equal(2, first.RngRoll.CandidateCount);
    Assert.NotEqual(first.RngRoll.SelectedIndex, second.RngRoll.SelectedIndex);
    Assert.NotEqual(firstPlay.CardInstanceId, secondPlay.CardInstanceId);
    Assert.Contains(firstPlay.CardInstanceId, new[] { left.CardInstanceId, right.CardInstanceId });
    Assert.Contains(secondPlay.CardInstanceId, new[] { left.CardInstanceId, right.CardInstanceId });
  }

  private static IReadOnlyDictionary<string, CardVersion> Versions(params CardVersion[] versions)
  {
    var result = new Dictionary<string, CardVersion>(StringComparer.Ordinal);
    foreach (var version in versions)
    {
      result[version.CardVersionId] = version;
    }

    return result;
  }

  private static InMemoryGameState CreateState(
    PlayerZones zones,
    string activePlayerId = "player-1",
    int mana = 0,
    IReadOnlyList<PlayerModifier>? modifiers = null,
    EntityInstance? weapon = null,
    EntityInstance? location = null,
    HeroPowerState? heroPower = null,
    IReadOnlyDictionary<string, int>? counters = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Mana = mana,
      MaxMana = mana,
      Modifiers = modifiers ?? [],
      Weapon = weapon,
      Location = location,
      HeroPower = heroPower,
      Zones = zones
    };

    return new InMemoryGameState(
      counters: counters,
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player,
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: activePlayerId,
      turnNumber: 1,
      phase: "TURN_START");
  }

  private static CardInstance CreateCardInstance(
    string cardInstanceId,
    CardType cardType = CardType.MinionCard) =>
    new(
      cardInstanceId,
      "card:river-guard",
      "card_river_guard_v1",
      "player-1",
      cardType);

  private static CardVersion CreateCardVersion(
    string cardVersionId,
    int manaCost,
    IReadOnlyList<string>? baseKeywords = null,
    CardType cardType = CardType.MinionCard,
    IReadOnlyList<EffectDefinition>? effects = null) =>
    new(
      "card:river-guard",
      cardVersionId,
      "card-dsl-0.2.0",
      "engine-bootstrap-0.3.0",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:resolved",
      FixedNow,
      new CardDefinition(
        "card:river-guard",
        cardVersionId,
        "card-dsl-0.2.0",
        "engine-bootstrap-0.3.0",
        "primitive-registry-bootstrap-0.2.0",
        "ruleset_mvp_core_v1",
        "catalog-alpha",
        new StaticCardData(
          "River Guard",
          cardType,
          ["mage"],
          Rarity.Common,
          manaCost,
          ["GUARD"],
          [],
          Collectible: true,
          ["mvp"]),
        new EntityDefinition(
          cardType is CardType.MinionCard or CardType.TokenCard
            ? new MinionDefinition(2, 3, ["GUARD"], baseKeywords ?? [], CanAttack: true)
            : null,
          cardType is CardType.SpellCard ? new SpellDefinition(null, RequiresTarget: false) : null,
          cardType is CardType.WeaponCard ? new WeaponDefinition(3, 2, baseKeywords ?? ["LIFESTEAL"]) : null,
          cardType is CardType.LocationCard ? new LocationDefinition(3, "TURN_START", "ONCE_PER_TURN") : null,
          cardType is CardType.HeroCard
            ? new HeroCardDefinition(
              5,
              new HeroPowerDefinition(2, "MAIN_PHASE", null, []),
              null)
            : null,
          cardType is CardType.HeroPowerCard
            ? new HeroPowerDefinition(1, "MAIN_PHASE", null, [])
            : null),
        effects ?? [],
        new CardTextGeneration("Vanilla minion.", null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata(null, null, null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static ResolvedRulesetProfile CreateProfile(
    IReadOnlyList<string> allowedCardTypes,
    RulesetLimits? limits = null) =>
    new(
      "ruleset_mvp_core_v1",
      "ruleset_mvp_core_v1",
      "0.2.0",
      FixedNow,
      "test",
      "sha256:resolved",
      "mvp",
      null,
      [],
      [],
      [],
      ["SIMPLE_PLAY_EFFECT"],
      [PlayCardCommand.CommandKind],
      [],
      ["HAND"],
      ["ON_PLAY"],
      [],
      allowedCardTypes,
      ["MINION"],
      ["COMMON"],
      ["mage"],
      ["GUARD"],
      limits ?? CreateLimits(),
      new ValidationPolicyRef("validation", true, true, true, true, true, true, true, true, false),
      new BalancePolicyRef("balance", false, "MEDIUM", [], 0.05, 30, 25, 0.1, 0, 0, "baseline", "seed", "none"),
      new SimulationPolicyRef("simulation", [], [], 0, 0, false, 30, 80),
      new PublicationPolicyRef("publication", true, false, false, false, false, false, false, ["INTERNAL"]),
      new LegacyPolicy(false, false, false, false, true));

  private static RulesetLimits CreateLimits(
    int maxBoardSummonsPerAction = 3,
    int maxManaCostReduction = 2,
    int maxBoardSize = 7,
    int maxHandSize = 10) =>
    new(30, 25, 20, 80, 5, 1, "LIMITED", 3, 2, 3, 3, maxBoardSummonsPerAction, 2, maxManaCostReduction, 3, 1, maxBoardSize, maxHandSize);

  private static EntityInstance CreateEntity(
    string entityInstanceId,
    string controllerPlayerId,
    int attack,
    int health,
    EntityType entityType = EntityType.Minion,
    IReadOnlyList<string>? keywords = null) =>
    new(
      entityInstanceId,
      $"card:{entityInstanceId}",
      controllerPlayerId,
      entityType,
      attack,
      health,
      Damage: 0,
      Keywords: keywords);

  private static PlayerModifier CreateCostModifier(string modifierId, int amount) =>
    new(
      modifierId,
      PlayerModifierTypes.CostModifier,
      amount,
      PlayerModifierDurations.NextPlay,
      Priority: 0,
      SourceRef: "test");

  private static EngineCapabilityRegistry CreateRegistryWithoutPlayCard()
  {
    var current = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    return current with
    {
      SupportedActionTypes = current.SupportedActionTypes
        .Where(action => !StringComparer.Ordinal.Equals(action, PlayCardCommand.CommandKind))
        .ToArray()
    };
  }
}
