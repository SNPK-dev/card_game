using Game.Application.Commands;
using Game.Application.Legality;
using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;
using Game.Shared.Ruleset;
using Xunit;

namespace Game.Application.Tests;

public sealed class CommandAdmissionTests
{
  private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-25T00:00:00Z");

  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void AdmissionAcceptsValidEnvelopeAsDeterministicPayload()
  {
    var result = new CommandAdmissionService().Admit(CreateEnvelope());

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
    Assert.Equal("draw", result.Payload.CommandType);
  }

  [Fact]
  public void AdmissionRejectsInvalidEnvelopeWithoutPayloadAcceptance()
  {
    var result = new CommandAdmissionService().Admit(CreateEnvelope(playerId: null));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    Assert.Contains(result.Errors, error => error.Code == CommandValidationCode.MissingPlayerId);
  }

  [Fact]
  public void AdmissionRejectsDebugCommandByDefault()
  {
    var result = new CommandAdmissionService().Admit(CreateEnvelope(
      playerId: "admin",
      commandType: "SET_STATE",
      commandCategory: CommandCategory.Debug,
      debugReplayFlag: true));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == CommandValidationCode.DebugCommandNotAllowed);
  }

  [Fact]
  public void AdmissionAcceptsLegalPlayCardAgainstOracleContext()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 2);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var envelope = CreatePlayCardEnvelope(card, attack: 2, health: 3, manaCost: 2);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
    Assert.Equal(PlayCardCommand.CommandKind, result.Payload.CommandType);
  }

  [Fact]
  public void AdmissionAcceptsPlayCardWithOracleDerivedCostReduction()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);
    var envelope = CreatePlayCardEnvelope(card, attack: 2, health: 3, manaCost: 1, costReduction: 2);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(2, result.Payload.Payload["costReduction"]);
  }

  [Fact]
  public void AdmissionRejectsPlayCardWhenCostReductionPayloadIsMissing()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);
    var envelope = CreatePlayCardEnvelope(card, attack: 2, health: 3, manaCost: 1);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("PLAY_CARD_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionRejectsWeaponPlayCardWhenDurabilityPayloadIsTampered()
  {
    var card = CreateCardInstance("weapon-instance-1", CardType.WeaponCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 4, cardType: CardType.WeaponCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 4);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 3,
      health: 0,
      manaCost: 4,
      entityInstanceId: "weapon:weapon-instance-1",
      durability: 1);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("PLAY_CARD_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionAcceptsLegalWeaponPlayCardAgainstOracleContext()
  {
    var card = CreateCardInstance("weapon-instance-1", CardType.WeaponCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 4, cardType: CardType.WeaponCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 4);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 3,
      health: 0,
      manaCost: 4,
      entityInstanceId: "weapon:weapon-instance-1",
      durability: 2,
      keywords: ["LIFESTEAL"]);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(2, result.Payload.Payload["durability"]);
    Assert.Equal("weapon:weapon-instance-1", result.Payload.Refs["entityInstanceId"]);
  }

  [Fact]
  public void AdmissionRejectsLocationPlayCardWhenDurabilityPayloadIsTampered()
  {
    var card = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, cardType: CardType.LocationCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 0,
      health: 0,
      manaCost: 2,
      entityInstanceId: "location:location-instance-1",
      durability: 2);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("PLAY_CARD_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionAcceptsLegalLocationPlayCardAgainstOracleContext()
  {
    var card = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 2, cardType: CardType.LocationCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 0,
      health: 0,
      manaCost: 2,
      entityInstanceId: "location:location-instance-1",
      durability: 3);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(3, result.Payload.Payload["durability"]);
    Assert.Equal("location:location-instance-1", result.Payload.Refs["entityInstanceId"]);
  }

  [Fact]
  public void AdmissionRejectsHeroCardWhenArmorPayloadIsTampered()
  {
    var card = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 7);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 0,
      health: 0,
      manaCost: 7,
      entityInstanceId: string.Empty,
      armorGain: 4,
      heroPowerCost: 2,
      heroPowerActivationTiming: "MAIN_PHASE");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("PLAY_CARD_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionAcceptsLegalHeroCardAgainstOracleContext()
  {
    var card = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(card.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 7);
    var envelope = CreatePlayCardEnvelope(
      card,
      attack: 0,
      health: 0,
      manaCost: 7,
      entityInstanceId: string.Empty,
      armorGain: 5,
      heroPowerCost: 2,
      heroPowerActivationTiming: "MAIN_PHASE");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(5, result.Payload.Payload["armorGain"]);
    Assert.Equal(2, result.Payload.Payload["heroPowerCost"]);
    Assert.Equal("MAIN_PHASE", result.Payload.Refs["heroPowerActivationTiming"]);
  }

  [Fact]
  public void AdmissionAcceptsLegalUseHeroPowerAgainstOracleContext()
  {
    var sourceCard = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"));
    var envelope = CreateUseHeroPowerEnvelope(sourceCard.CardInstanceId, manaCost: 2);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(UseHeroPowerCommand.CommandKind, result.Payload.CommandType);
    Assert.Equal(sourceCard.CardInstanceId, result.Payload.Refs["sourceCardInstanceId"]);
  }

  [Fact]
  public void AdmissionRejectsUseHeroPowerWhenManaCostIsTampered()
  {
    var sourceCard = CreateCardInstance("hero-card-instance-1", CardType.HeroCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 7, cardType: CardType.HeroCard);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"));
    var envelope = CreateUseHeroPowerEnvelope(sourceCard.CardInstanceId, manaCost: 1);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("USE_HERO_POWER_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionAcceptsLegalActivateLocationAgainstOracleContext()
  {
    var sourceCard = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 2, cardType: CardType.LocationCard);
    var location = new EntityInstance(
      "location:location-instance-1",
      sourceCard.CardInstanceId,
      "player-1",
      EntityType.Location,
      Attack: 0,
      Health: 3,
      Damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Location = location,
          Zones = new PlayerZones(setAside: [sourceCard])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var envelope = CreateActivateLocationEnvelope(location.EntityInstanceId, sourceCard.CardInstanceId);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Equal(ActivateLocationCommand.CommandKind, result.Payload.CommandType);
    Assert.Equal(location.EntityInstanceId, result.Payload.Refs["locationEntityInstanceId"]);
    Assert.Equal(sourceCard.CardInstanceId, result.Payload.Refs["sourceCardInstanceId"]);
  }

  [Fact]
  public void AdmissionRejectsActivateLocationWhenTargetIsTampered()
  {
    var sourceCard = CreateCardInstance("location-instance-1", CardType.LocationCard);
    var version = CreateCardVersion(sourceCard.CardVersion, manaCost: 2, cardType: CardType.LocationCard);
    var location = new EntityInstance(
      "location:location-instance-1",
      sourceCard.CardInstanceId,
      "player-1",
      EntityType.Location,
      Attack: 0,
      Health: 3,
      Damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Location = location,
          Zones = new PlayerZones(setAside: [sourceCard])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var envelope = CreateActivateLocationEnvelope(
      location.EntityInstanceId,
      sourceCard.CardInstanceId,
      targetPlayerId: "player-2",
      targetEntityInstanceId: "missing-target");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("ACTIVATE_LOCATION_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionRejectsIllegalPlayCardBeforePayloadAcceptance()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 3);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 2);
    var envelope = CreatePlayCardEnvelope(card, attack: 2, health: 3, manaCost: 3);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    Assert.Contains(result.Errors, error =>
      error.Code == CommandValidationCode.IllegalCommand
      && error.Message.Contains("INSUFFICIENT_MANA", StringComparison.Ordinal)
      && error.Message.Contains("path=hand[card-instance-1].manaCost", StringComparison.Ordinal)
      && error.Message.Contains("identifier=3", StringComparison.Ordinal));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void AdmissionRejectsPlayCardWhenRulesetBoardSummonLimitBlocksMaterialization()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);
    var profile = TestRulesetProfileFactory.CreateProfile(
      "ruleset_mvp_core_v1",
      resolvedHash: "sha256:resolved",
      allowedActions: [PlayCardCommand.CommandKind]) with
    {
      Limits = CreateLimits(maxBoardSummonsPerAction: 0)
    };
    var envelope = CreatePlayCardEnvelope(card, attack: 2, health: 3, manaCost: 0);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version), profile));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("RULESET_BOARD_SUMMON_LIMIT_REACHED", error.Message, StringComparison.Ordinal);
    Assert.Contains("path=rulesetProfile.limits.maxBoardSummonsPerAction", error.Message, StringComparison.Ordinal);
    Assert.Contains("identifier=0", error.Message, StringComparison.Ordinal);
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void AdmissionRejectsPlayCardPayloadThatDoesNotMatchLegalCommand()
  {
    var card = CreateCardInstance("card-instance-1");
    var version = CreateCardVersion(card.CardVersion, manaCost: 0);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 10);
    var envelope = CreatePlayCardEnvelope(card, attack: 99, health: 3, manaCost: 0);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", Versions(version)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("PLAY_CARD_PAYLOAD_MISMATCH", error.Message, StringComparison.Ordinal);
    Assert.Contains("path=payload", error.Message, StringComparison.Ordinal);
    Assert.Contains("identifier=card-instance-1", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionAcceptsLegalAttackAgainstOracleContext()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var hero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = hero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");
    var envelope = CreateAttackEnvelope(attacker.EntityInstanceId, "player-2", hero.EntityInstanceId);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
  }

  [Fact]
  public void AdmissionRejectsAttackTargetNotReturnedByOracleWithTargetDiagnostic()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var hero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = hero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");
    var envelope = CreateAttackEnvelope(attacker.EntityInstanceId, "player-2", "missing-target");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("ATTACK_TARGET_NOT_LEGAL", error.Message, StringComparison.Ordinal);
    Assert.Contains("path=refs.targetEntityInstanceId", error.Message, StringComparison.Ordinal);
    Assert.Contains("identifier=missing-target", error.Message, StringComparison.Ordinal);
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void AdmissionRejectsAttackBlockedBySummonSickness()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 2, health: 3);
    var hero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, entityType: EntityType.Hero);
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
          Hero = hero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 2,
      phase: "TURN_START");
    var envelope = CreateAttackEnvelope(attacker.EntityInstanceId, "player-2", hero.EntityInstanceId);

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    Assert.Contains(result.Errors, error =>
      error.Code == CommandValidationCode.IllegalCommand
      && error.Message.Contains("SUMMON_SICKNESS", StringComparison.Ordinal));
  }

  [Fact]
  public void AdmissionAcceptsResolveChoiceAgainstPendingChoiceState()
  {
    var state = CreateStateWithChoice();
    var envelope = CreateResolveChoiceEnvelope(
      "choice-spell",
      "choice-spell:option:0002",
      "card-discovered-1",
      "card:arcane-b:v1",
      "SPELL_CARD");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
    Assert.Equal(CommandCategory.Choice, result.Payload.CommandCategory);
  }

  [Fact]
  public void AdmissionAcceptsResolveChoiceAgainstPendingEffectTargetChoiceState()
  {
    var state = CreateStateWithEffectTargetChoice();
    var envelope = CreateResolveChoiceEnvelope(
      "choice-effect-target",
      "choice-effect-target:option:0001",
      "effect-target-placeholder-card",
      "effect_target_placeholder_v1",
      "SPELL_CARD");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
    Assert.Equal(CommandCategory.Choice, result.Payload.CommandCategory);
  }

  [Fact]
  public void AdmissionAcceptsResolveChoiceWithMultipleEffectTargetOptions()
  {
    var state = CreateStateWithEffectTargetChoice();
    var envelope = CreateResolveChoiceEnvelope(
      "choice-effect-target",
      "choice-effect-target:option:0001",
      "effect-target-placeholder-card",
      "effect_target_placeholder_v1",
      "SPELL_CARD",
      "choice-effect-target:option:0001,choice-effect-target:option:0002");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.True(result.IsAccepted);
    Assert.NotNull(result.Payload);
    Assert.Empty(result.Errors);
    Assert.Equal(
      "choice-effect-target:option:0001,choice-effect-target:option:0002",
      result.Payload.Refs["selectedOptionIds"]);
  }

  [Fact]
  public void AdmissionRejectsResolveChoiceForUnavailableOption()
  {
    var state = CreateStateWithChoice();
    var envelope = CreateResolveChoiceEnvelope(
      "choice-spell",
      "choice-spell:option:9999",
      "card-discovered-1",
      "card:arcane-b:v1",
      "SPELL_CARD");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("CHOICE_OPTION_NOT_FOUND", error.Message, StringComparison.Ordinal);
    Assert.Contains("path=refs.selectedOptionId", error.Message, StringComparison.Ordinal);
    Assert.Contains("identifier=choice-spell:option:9999", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void AdmissionRejectsResolveChoiceWhenCardInstanceAlreadyExists()
  {
    var existing = new CardInstance(
      "card-discovered-1",
      "card:existing",
      "card:existing:v1",
      "player-1",
      CardType.SpellCard);
    var state = CreateStateWithChoice(new PlayerZones(hand: [existing]));
    var envelope = CreateResolveChoiceEnvelope(
      "choice-spell",
      "choice-spell:option:0002",
      existing.CardInstanceId,
      "card:arcane-b:v1",
      "SPELL_CARD");

    var result = new CommandAdmissionService().Admit(
      envelope,
      new LegalActionQuery(state, "player-1", new Dictionary<string, CardVersion>(StringComparer.Ordinal)));

    Assert.False(result.IsAccepted);
    Assert.Null(result.Payload);
    var error = Assert.Single(result.Errors);
    Assert.Equal(CommandValidationCode.IllegalCommand, error.Code);
    Assert.Contains("CARD_INSTANCE_ALREADY_EXISTS", error.Message, StringComparison.Ordinal);
    Assert.Contains("path=refs.cardInstanceId", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ReplayValidationRejectsInvalidPayloadBeforeMutation()
  {
    var initialState = new InMemoryGameState();
    var invalidLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(2, null, "play")
    ]);
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);

    Assert.Throws<InvalidOperationException>(() => replayer.Replay(initialState, invalidLog));
    Assert.Equal(0, initialState.Revision);
  }

  [Fact]
  public void ReplayUsesSameValidationContractForDebugPolicy()
  {
    var debugLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "admin",
        "SET_STATE",
        commandCategory: CommandCategory.Debug,
        debugReplayFlag: true)
    ]);
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);

    var replay = replayer.Replay(new InMemoryGameState(), debugLog);

    Assert.Equal(1, replay.State.Revision);
  }

  private static GameCommandEnvelope CreateEnvelope(
    string? playerId = "player-1",
    string commandType = "draw",
    CommandCategory commandCategory = CommandCategory.Player,
    bool debugReplayFlag = false)
  {
    return new GameCommandEnvelope(
      "command-1",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      playerId,
      commandType,
      null,
      null,
      expectedPreStateHash: null,
      commandCategory,
      debugReplayFlag);
  }

  private static GameCommandEnvelope CreatePlayCardEnvelope(
    CardInstance card,
    int attack,
    int health,
    int manaCost,
    int costReduction = 0,
    IReadOnlyList<string>? keywords = null,
    string? entityInstanceId = null,
    int durability = 0,
    int armorGain = 0,
    int? heroPowerCost = null,
    string? heroPowerActivationTiming = null)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["cardInstanceId"] = card.CardInstanceId
    };
    var resolvedEntityInstanceId = entityInstanceId ?? $"entity:{card.CardInstanceId}";
    if (!string.IsNullOrWhiteSpace(resolvedEntityInstanceId))
    {
      refs["entityInstanceId"] = resolvedEntityInstanceId;
    }
    foreach (var keyword in (keywords ?? []).Order(StringComparer.Ordinal).Select((value, index) => new { value, index }))
    {
      refs[$"entityKeyword:{keyword.index + 1:0000}"] = keyword.value;
    }

    var payload = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
      ["attack"] = attack,
      ["health"] = health,
      ["manaCost"] = manaCost
    };
    if (costReduction > 0)
    {
      payload["costReduction"] = costReduction;
    }

    if (durability > 0)
    {
      payload["durability"] = durability;
    }

    if (armorGain > 0)
    {
      payload["armorGain"] = armorGain;
    }

    if (heroPowerCost.HasValue)
    {
      payload["heroPowerCost"] = heroPowerCost.Value;
    }

    if (!string.IsNullOrWhiteSpace(heroPowerActivationTiming))
    {
      refs["heroPowerActivationTiming"] = heroPowerActivationTiming;
    }

    return new GameCommandEnvelope(
      "command-play-card",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      "player-1",
      PlayCardCommand.CommandKind,
      payload,
      refs);
  }

  private static GameCommandEnvelope CreateAttackEnvelope(
    string attackerEntityInstanceId,
    string targetPlayerId,
    string targetEntityInstanceId) =>
    new(
      "command-attack",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      "player-1",
      AttackCommand.CommandKind,
      null,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["attackerEntityInstanceId"] = attackerEntityInstanceId,
        ["targetEntityInstanceId"] = targetEntityInstanceId,
        ["targetPlayerId"] = targetPlayerId
      });

  private static GameCommandEnvelope CreateResolveChoiceEnvelope(
    string choiceId,
    string selectedOptionId,
    string cardInstanceId,
    string cardVersion,
    string cardType,
    string? selectedOptionIds = null)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["cardInstanceId"] = cardInstanceId,
      ["cardType"] = cardType,
      ["cardVersion"] = cardVersion,
      ["choiceId"] = choiceId,
      ["selectedOptionId"] = selectedOptionId
    };
    if (!string.IsNullOrWhiteSpace(selectedOptionIds))
    {
      refs["selectedOptionIds"] = selectedOptionIds;
    }

    return new(
      "command-resolve-choice",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      "player-1",
      ResolveChoiceCommand.CommandKind,
      null,
      refs,
      commandCategory: CommandCategory.Choice);
  }

  private static GameCommandEnvelope CreateUseHeroPowerEnvelope(
    string sourceCardInstanceId,
    int manaCost,
    string? targetPlayerId = null,
    string? targetEntityInstanceId = null)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["sourceCardInstanceId"] = sourceCardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(targetPlayerId))
    {
      refs["targetPlayerId"] = targetPlayerId;
    }

    if (!string.IsNullOrWhiteSpace(targetEntityInstanceId))
    {
      refs["targetEntityInstanceId"] = targetEntityInstanceId;
    }

    return new GameCommandEnvelope(
      "command-use-hero-power",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      "player-1",
      UseHeroPowerCommand.CommandKind,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["manaCost"] = manaCost
      },
      refs);
  }

  private static GameCommandEnvelope CreateActivateLocationEnvelope(
    string locationEntityInstanceId,
    string sourceCardInstanceId,
    string? targetPlayerId = null,
    string? targetEntityInstanceId = null)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["locationEntityInstanceId"] = locationEntityInstanceId,
      ["sourceCardInstanceId"] = sourceCardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(targetPlayerId))
    {
      refs["targetPlayerId"] = targetPlayerId;
    }

    if (!string.IsNullOrWhiteSpace(targetEntityInstanceId))
    {
      refs["targetEntityInstanceId"] = targetEntityInstanceId;
    }

    return new GameCommandEnvelope(
      "command-activate-location",
      new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero),
      null,
      "correlation-1",
      "session-1",
      null,
      "raw-1",
      1,
      "player-1",
      ActivateLocationCommand.CommandKind,
      null,
      refs);
  }

  private static IReadOnlyDictionary<string, CardVersion> Versions(CardVersion version) =>
    new Dictionary<string, CardVersion>(StringComparer.Ordinal)
    {
      [version.CardVersionId] = version
    };

  private static InMemoryGameState CreateState(
    PlayerZones zones,
    int mana,
    IReadOnlyList<PlayerModifier>? modifiers = null,
    HeroPowerState? heroPower = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Mana = mana,
      MaxMana = mana,
      Modifiers = modifiers ?? [],
      HeroPower = heroPower,
      Zones = zones
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player,
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
  }

  private static InMemoryGameState CreateStateWithChoice(PlayerZones? zones = null)
  {
    var choice = new PendingChoice(
      "choice-spell",
      "player-1",
      "DISCOVER",
      "PUBLIC",
      "HAND",
      "effect:test",
      [
        new ChoiceOption("choice-spell:option:0001", "CARD", "card:arcane-a"),
        new ChoiceOption("choice-spell:option:0002", "CARD", "card:arcane-b")
      ]);

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = zones ?? PlayerZones.Empty
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      pendingChoices: new SortedDictionary<string, PendingChoice>(StringComparer.Ordinal)
      {
        [choice.ChoiceId] = choice
      });
  }

  private static InMemoryGameState CreateStateWithEffectTargetChoice()
  {
    var choice = new PendingChoice(
      "choice-effect-target",
      "player-1",
      "EFFECT_TARGET",
      "PUBLIC",
      "TARGET",
      "effect-choice-v1|DEATHRATTLE|player-1|card-source|card-source-v1|effect-bolt|ENTITY_INSTANCE|player-1|entity-source",
      [
        new ChoiceOption(
          "choice-effect-target:option:0001",
          "TARGET",
          "target-v1|HERO|player-2|hero-player-2"),
        new ChoiceOption(
          "choice-effect-target:option:0002",
          "TARGET",
          "target-v1|ENTITY_INSTANCE|player-2|entity-player-2")
      ]);

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1"),
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      pendingChoices: new SortedDictionary<string, PendingChoice>(StringComparer.Ordinal)
      {
        [choice.ChoiceId] = choice
      });
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
    CardType cardType = CardType.MinionCard) =>
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
            ? new MinionDefinition(2, 3, ["GUARD"], [], CanAttack: true)
            : null,
          null,
          cardType is CardType.WeaponCard ? new WeaponDefinition(3, 2, ["LIFESTEAL"]) : null,
          cardType is CardType.LocationCard ? new LocationDefinition(3, "TURN_START", "ONCE_PER_TURN") : null,
          cardType is CardType.HeroCard
            ? new HeroCardDefinition(
              5,
              new HeroPowerDefinition(2, "MAIN_PHASE", null, []),
              null)
            : null,
          null),
        [],
        new CardTextGeneration("Vanilla minion.", null),
        new BalanceMetadata(1, 0, []),
        new AssetMetadata(null, null, null),
        new AuthoringMetadata("test", FixedNow, "pipeline-v1", [])));

  private static EntityInstance CreateEntity(
    string entityInstanceId,
    string controllerPlayerId,
    int attack,
    int health,
    EntityType entityType = EntityType.Minion) =>
    new(
      entityInstanceId,
      $"card:{entityInstanceId}",
      controllerPlayerId,
      entityType,
      attack,
      health,
      Damage: 0);

  private static RulesetLimits CreateLimits(int maxBoardSummonsPerAction) =>
    new(30, 25, 20, 80, 5, 1, "LIMITED", 3, 2, 3, 3, maxBoardSummonsPerAction, 2, 2, 3, 1);

  private static PlayerModifier CreateCostModifier(string modifierId, int amount) =>
    new(
      modifierId,
      PlayerModifierTypes.CostModifier,
      amount,
      PlayerModifierDurations.NextPlay,
      Priority: 0,
      SourceRef: "test");
}
