using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class BasicActionExecutionTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void DrawCardActionProducesBatchWithoutMutatingState()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(deck: [card]));
    var executor = new DrawCardActionExecutor("player-1", card.CardInstanceId);
    var context = CreateContext(state, "draw");

    var batch = executor.Execute(context);

    Assert.Single(state.Players["player-1"].Zones.Deck);
    Assert.Empty(state.Players["player-1"].Zones.Hand);
    Assert.Equal(DrawCardMutationHandler.SupportedMutationType, batch.Mutations.Single().MutationType);
    Assert.Equal(_hasher.HashGameplayState(state), batch.PreStateHash);
  }

  [Fact]
  public void DrawCardMutationMovesCardFromDeckToHand()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(deck: [card]));
    var batch = new DrawCardActionExecutor("player-1", card.CardInstanceId)
      .Execute(CreateContext(state, "draw"));

    var application = new StateMutator().Apply(state, batch);

    var player = application.State.Players["player-1"];
    Assert.Empty(player.Zones.Deck);
    Assert.Single(player.Zones.Hand);
    Assert.Equal(card.CardInstanceId, player.Zones.Hand.Single().CardInstanceId);
  }

  [Fact]
  public void SummonEntityActionAddsBoardEntityThroughMutationHandler()
  {
    var state = CreateState(PlayerZones.Empty);
    var executor = new SummonEntityActionExecutor(
      new SummonEntityActionRequest(
        "player-1",
        "entity-1",
        "card-instance-1",
        Attack: 2,
        Health: 3));

    var batch = executor.Execute(CreateContext(state, "summon"));
    var application = new StateMutator().Apply(state, batch);

    Assert.Empty(state.Players["player-1"].Zones.Board);
    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal("entity-1", entity.EntityInstanceId);
    Assert.Equal(2, entity.Attack);
    Assert.Equal(3, entity.Health);
    Assert.Equal(0, entity.Damage);
  }

  [Fact]
  public void DamageAndHealActionsUpdateEntityDamageThroughMutationHandlers()
  {
    var entity = CreateEntity("entity-1", damage: 0);
    var state = CreateState(new PlayerZones(board: [entity]));
    var damaged = new StateMutator()
      .Apply(state, new DealDamageActionExecutor("player-1", entity.EntityInstanceId, amount: 3)
        .Execute(CreateContext(state, "damage")))
      .State;

    var healed = new StateMutator()
      .Apply(damaged, new HealEntityActionExecutor("player-1", entity.EntityInstanceId, amount: 2)
        .Execute(CreateContext(damaged, "heal")))
      .State;

    Assert.Equal(0, state.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Equal(3, damaged.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Equal(1, healed.Players["player-1"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void DamageMutationCanDamageHero()
  {
    var hero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = CreateState(
      PlayerZones.Empty,
      player2: PlayerState.Empty("player-2") with
      {
        Hero = hero
      });
    var batch = new DealDamageActionExecutor("player-2", hero.EntityInstanceId, amount: 4)
      .Execute(CreateContext(state, "damage"));

    var damaged = new StateMutator().Apply(state, batch).State;

    Assert.Equal(4, damaged.Players["player-2"].Hero?.Damage);
  }

  [Fact]
  public void HealDoesNotReduceEntityDamageBelowZero()
  {
    var entity = CreateEntity("entity-1", damage: 1);
    var state = CreateState(new PlayerZones(board: [entity]));
    var batch = new HealEntityActionExecutor("player-1", entity.EntityInstanceId, amount: 4)
      .Execute(CreateContext(state, "heal"));

    var healed = new StateMutator().Apply(state, batch).State;

    Assert.Equal(0, healed.Players["player-1"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void PlayCardActionProducesOrderedMoveAndSummonBatchWithoutMutatingState()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]));
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3);
    var executor = new PlayCardActionExecutor();

    var batch = executor.Execute(CreateContext(state, command));

    Assert.Single(state.Players["player-1"].Zones.Hand);
    Assert.Empty(state.Players["player-1"].Zones.Board);
    Assert.Equal(
      [
        MoveCardZoneMutationHandler.SupportedMutationType,
        SummonEntityMutationHandler.SupportedMutationType,
        IncrementCounterMutationHandler.SupportedMutationType
      ],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.DoesNotContain(
      "PostStateHash",
      typeof(ProposedMutationBatch).GetProperties().Select(property => property.Name));
  }

  [Fact]
  public void PlayCardActionMovesCardToSetAsideAndSummonsEntityThroughStateMutator()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]), mana: 5);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 2);
    var batch = new PlayCardActionExecutor().Execute(CreateContext(state, command));

    var application = new StateMutator().Apply(state, batch);

    var player = application.State.Players["player-1"];
    Assert.Equal(3, player.Mana);
    Assert.Empty(player.Zones.Hand);
    Assert.Single(player.Zones.SetAside);
    Assert.Equal(card.CardInstanceId, player.Zones.SetAside.Single().CardInstanceId);
    var entity = Assert.Single(player.Zones.Board);
    Assert.Equal("entity-1", entity.EntityInstanceId);
    Assert.Equal(card.CardInstanceId, entity.SourceCardInstanceId);
  }

  [Fact]
  public void PlayCardActionMaterializesEntityKeywordsThroughMutationPipeline()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]), mana: 5);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 0,
      Keywords: ["STEALTH", "TAUNT"]);

    var application = new StateMutator().Apply(
      state,
      new PlayCardActionExecutor().Execute(CreateContext(state, command)));

    var entity = Assert.Single(application.State.Players["player-1"].Zones.Board);
    Assert.Equal(["STEALTH", "TAUNT"], entity.Keywords);
  }

  [Fact]
  public void PlayCardActionRejectsSummonWhenBoardCapacityMetadataIsFull()
  {
    var card = CreateCard("card-instance-1");
    var existing = CreateEntity("entity-existing", damage: 0);
    var state = CreateState(new PlayerZones(hand: [card], board: [existing]), mana: 5);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 0,
      MaxBoardSize: 1);

    Assert.Throws<InvalidOperationException>(() =>
      new StateMutator().Apply(
        state,
        new PlayCardActionExecutor().Execute(CreateContext(state, command))));
  }

  [Fact]
  public void SummonMutationRejectsMalformedBoardCapacityMetadata()
  {
    var state = CreateState(PlayerZones.Empty, mana: 5);
    var mutation = new StateMutation(
      "mutation:1",
      SummonEntityMutationHandler.SupportedMutationType,
      "test",
      "entity-1",
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["attack"] = 2,
        ["health"] = 3
      },
      "ON_PLAY",
      1,
      null,
      "001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = "entity-1",
        ["entityType"] = "MINION",
        ["maxBoardSize"] = "not-a-number",
        ["playerId"] = "player-1",
        ["sourceCardInstanceId"] = "card-instance-1"
      });

    Assert.False(new SummonEntityMutationHandler().CanApply(state, mutation));
  }

  [Fact]
  public void PaidPlayCardActionPaysManaThroughMutationPipeline()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]), mana: 5);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 2);

    var batch = new PlayCardActionExecutor().Execute(CreateContext(state, command));
    var application = new StateMutator().Apply(state, batch);

    Assert.Equal(
      [
        ModifyManaMutationHandler.SupportedMutationType,
        MoveCardZoneMutationHandler.SupportedMutationType,
        SummonEntityMutationHandler.SupportedMutationType,
        IncrementCounterMutationHandler.SupportedMutationType
      ],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(3, application.State.Players["player-1"].Mana);
    Assert.Equal(
      state.TurnNumber,
      application.State.Counters[CombatStateKeys.SummonCounterKey("player-1", "entity-1")]);
  }

  [Fact]
  public void PlayCardActionAppliesAndConsumesNextPlayCostModifier()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-1", amount: 2)]);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 1,
      CostReduction: 2);

    var batch = new PlayCardActionExecutor().Execute(CreateContext(state, command));
    var application = new StateMutator().Apply(state, batch);

    Assert.Contains(batch.Mutations, mutation => mutation.MutationType == RemoveModifierMutationHandler.SupportedMutationType);
    Assert.Equal(0, application.State.Players["player-1"].Mana);
    Assert.Empty(application.State.Players["player-1"].Modifiers);
    Assert.Single(application.State.Players["player-1"].Zones.Board);
  }

  [Fact]
  public void PlayCardActionAppliesRestOfTurnCostModifierWithoutConsumingIt()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(
      new PlayerZones(hand: [card]),
      mana: 1,
      modifiers: [CreateCostModifier("modifier-rest-turn", amount: 2, PlayerModifierDurations.RestOfTurn)]);
    var command = new PlayCardCommand(
      "player-1",
      card.CardInstanceId,
      "entity-1",
      Attack: 2,
      Health: 3,
      ManaCost: 1,
      CostReduction: 2);

    var batch = new PlayCardActionExecutor().Execute(CreateContext(state, command));
    var application = new StateMutator().Apply(state, batch);

    Assert.DoesNotContain(batch.Mutations, mutation => mutation.MutationType == RemoveModifierMutationHandler.SupportedMutationType);
    Assert.Equal(0, application.State.Players["player-1"].Mana);
    Assert.Contains(application.State.Players["player-1"].Modifiers, modifier =>
      StringComparer.Ordinal.Equals(modifier.ModifierId, "modifier-rest-turn"));
    Assert.Single(application.State.Players["player-1"].Zones.Board);
  }

  [Fact]
  public void EndTurnActionExpiresRestOfTurnCostModifiersThroughMutationPipeline()
  {
    var drawCard = CreateCard("player-2-card-1");
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(deck: [drawCard])
      },
      modifiers: [CreateCostModifier("modifier-rest-turn", amount: 2, PlayerModifierDurations.RestOfTurn)]);

    var batch = new EndTurnActionExecutor().Execute(CreateContext(state, new EndTurnCommand("player-1")));
    var application = new StateMutator().Apply(state, batch);
    var committed = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);

    Assert.Equal(
      [RemoveModifierMutationHandler.SupportedMutationType, AdvanceTurnMutationHandler.SupportedMutationType, DrawCardMutationHandler.SupportedMutationType],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.Empty(application.State.Players["player-1"].Modifiers);
    Assert.Contains(committed, @event => @event.EventType == CommittedEventType.ModifierExpired);
    Assert.Equal("player-2", application.State.ActivePlayerId);
  }

  [Fact]
  public void EndTurnActionProducesBatchWithoutMutatingState()
  {
    var drawCard = CreateCard("player-2-card-1");
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(deck: [drawCard])
      });
    var executor = new EndTurnActionExecutor();

    var batch = executor.Execute(CreateContext(state, new EndTurnCommand("player-1")));

    Assert.Equal("player-1", state.ActivePlayerId);
    Assert.Equal(1, state.TurnNumber);
    Assert.Equal(
      [AdvanceTurnMutationHandler.SupportedMutationType, DrawCardMutationHandler.SupportedMutationType],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(_hasher.HashGameplayState(state), batch.PreStateHash);
  }

  [Fact]
  public void EndTurnActionAdvancesActivePlayerThroughStateMutator()
  {
    var drawCard = CreateCard("player-2-card-1");
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(deck: [drawCard])
      });
    var batch = new EndTurnActionExecutor().Execute(CreateContext(state, new EndTurnCommand("player-1")));

    var application = new StateMutator().Apply(state, batch);

    Assert.Equal("player-2", application.State.ActivePlayerId);
    Assert.Equal(1, application.State.TurnNumber);
    Assert.Equal("TURN_START", application.State.Phase);
    Assert.Empty(application.State.Players["player-2"].Zones.Deck);
    Assert.Equal(drawCard.CardInstanceId, application.State.Players["player-2"].Zones.Hand.Single().CardInstanceId);
  }

  [Fact]
  public void EndTurnActionBurnsDrawToGraveyardWhenHandCapacityIsFull()
  {
    var drawCard = CreateCard("player-2-card-1");
    var handCard = new CardInstance("player-2-hand-1", "card:hand", "1.0.0", "player-2", CardType.MinionCard);
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(deck: [drawCard], hand: [handCard])
      });

    var batch = new EndTurnActionExecutor().Execute(CreateContext(
      state,
      new EndTurnCommand("player-1", MaxHandSize: 1)));
    var application = new StateMutator().Apply(state, batch);

    Assert.Empty(application.State.Players["player-2"].Zones.Deck);
    Assert.Equal(handCard.CardInstanceId, application.State.Players["player-2"].Zones.Hand.Single().CardInstanceId);
    Assert.Equal(drawCard.CardInstanceId, application.State.Players["player-2"].Zones.Graveyard.Single().CardInstanceId);
    var events = new CommittedEventFactory().CreateCommittedEvents(application.AppliedBatch, 1);
    Assert.Contains(events, @event => @event.EventType == CommittedEventType.CardDiscarded);
    var replayed = new StateMutator().Apply(state, batch);
    Assert.Equal(application.AppliedBatch.PostStateHash, replayed.AppliedBatch.PostStateHash);
  }

  [Fact]
  public void DrawMutationRejectsMalformedHandCapacityMetadata()
  {
    var drawCard = CreateCard("player-1-card-1");
    var state = CreateState(new PlayerZones(deck: [drawCard]), mana: 5);
    var mutation = new StateMutation(
      "mutation:1",
      DrawCardMutationHandler.SupportedMutationType,
      "test",
      drawCard.CardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "TURN_START",
      1,
      null,
      "001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = drawCard.CardInstanceId,
        ["maxHandSize"] = "not-a-number",
        ["playerId"] = "player-1"
      });

    Assert.False(new DrawCardMutationHandler().CanApply(state, mutation));
  }

  [Fact]
  public void EndTurnActionAppliesFatigueWhenNextPlayerDeckIsEmpty()
  {
    var hero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Hero = hero,
        FatigueCounter = 2
      });

    var batch = new EndTurnActionExecutor().Execute(CreateContext(state, new EndTurnCommand("player-1")));
    var application = new StateMutator().Apply(state, batch);

    Assert.Equal(
      [
        AdvanceTurnMutationHandler.SupportedMutationType,
        ModifyFatigueMutationHandler.SupportedMutationType,
        ApplyDamageMutationHandler.SupportedMutationType
      ],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.Equal(3, application.State.Players["player-2"].FatigueCounter);
    Assert.Equal(3, application.State.Players["player-2"].Hero?.Damage);
  }

  [Fact]
  public void AttackActionDamagesOpponentHeroAndMarksAttacker()
  {
    var attacker = CreateEntity("entity-attacker", damage: 0);
    var defenderHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = defenderHero
      });
    var command = new AttackCommand(
      "player-1",
      attacker.EntityInstanceId,
      "player-2",
      defenderHero.EntityInstanceId);

    var batch = new AttackActionExecutor().Execute(CreateContext(state, command));
    var application = new StateMutator().Apply(state, batch);

    Assert.Equal(2, application.State.Players["player-2"].Hero?.Damage);
    Assert.Equal(
      1,
      application.State.Counters[AttackActionExecutor.AttackCounterKey(
        state.TurnNumber,
        "player-1",
        attacker.EntityInstanceId)]);
  }

  [Fact]
  public void AttackActionDamagesBothMinionsWhenTargetIsMinion()
  {
    var attacker = CreateEntity("entity-attacker", damage: 0);
    var defender = new EntityInstance(
      "entity-defender",
      "card-instance-2",
      "player-2",
      EntityType.Minion,
      Attack: 3,
      Health: 5,
      Damage: 0);
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(board: [defender])
      });
    var command = new AttackCommand(
      "player-1",
      attacker.EntityInstanceId,
      "player-2",
      defender.EntityInstanceId);

    var application = new StateMutator().Apply(
      state,
      new AttackActionExecutor().Execute(CreateContext(state, command)));

    Assert.Equal(3, application.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Equal(2, application.State.Players["player-2"].Zones.Board.Single().Damage);
  }

  [Fact]
  public void AttackActionRequiresTauntTargetWhenTargetableTauntExists()
  {
    var attacker = CreateEntity("entity-attacker", damage: 0);
    var taunt = CreateEntity("entity-taunt", damage: 0, controllerPlayerId: "player-2", keywords: ["TAUNT"]);
    var other = CreateEntity("entity-other", damage: 0, controllerPlayerId: "player-2");
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(board: [other, taunt])
      });

    Assert.Throws<InvalidOperationException>(() =>
      new AttackActionExecutor().Execute(CreateContext(
        state,
        new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", other.EntityInstanceId))));
  }

  [Fact]
  public void AttackActionRejectsStealthedTarget()
  {
    var attacker = CreateEntity("entity-attacker", damage: 0);
    var stealthed = CreateEntity("entity-stealth", damage: 0, controllerPlayerId: "player-2", keywords: ["STEALTH"]);
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(board: [stealthed])
      });

    Assert.Throws<InvalidOperationException>(() =>
      new AttackActionExecutor().Execute(CreateContext(
        state,
        new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", stealthed.EntityInstanceId))));
  }

  [Fact]
  public void AttackActionRejectsMinionSummonedThisTurn()
  {
    var attacker = CreateEntity("entity-attacker", damage: 0);
    var defenderHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = new InMemoryGameState(
      counters: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        [CombatStateKeys.SummonCounterKey("player-1", attacker.EntityInstanceId)] = 1
      },
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = defenderHero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var command = new AttackCommand(
      "player-1",
      attacker.EntityInstanceId,
      "player-2",
      defenderHero.EntityInstanceId);

    Assert.Throws<InvalidOperationException>(() =>
      new AttackActionExecutor().Execute(CreateContext(state, command)));
  }

  [Fact]
  public void ConcedeActionSetsTerminalStateThroughMutationPipeline()
  {
    var state = CreateState(PlayerZones.Empty);
    var command = new ConcedeCommand("player-1");

    var application = new StateMutator().Apply(
      state,
      new ConcedeActionExecutor().Execute(CreateContext(state, command)));

    Assert.True(application.State.TerminalState.IsTerminal);
    Assert.Equal("CONCEDED", application.State.TerminalState.Reason);
    Assert.Equal("player-2", application.State.TerminalState.WinningPlayerId);
    Assert.Equal("player-1", application.State.TerminalState.LosingPlayerId);
  }

  [Fact]
  public void ResolveChoiceActionCreatesSelectedCardInHand()
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
    var state = CreateState(PlayerZones.Empty, pendingChoices: new SortedDictionary<string, PendingChoice>(StringComparer.Ordinal)
    {
      [choice.ChoiceId] = choice
    });
    var command = new ResolveChoiceCommand(
      "player-1",
      "choice-spell",
      "choice-spell:option:0002",
      "card-discovered-1",
      "card:arcane-b:v1",
      CardType.SpellCard);

    var batch = new ResolveChoiceActionExecutor().Execute(CreateContext(state, command));
    var application = new StateMutator().Apply(state, batch);

    Assert.Equal(
      [ResolveChoiceMutationHandler.SupportedMutationType, CreateCardInstanceMutationHandler.SupportedMutationType],
      batch.Mutations.Select(mutation => mutation.MutationType));
    Assert.Empty(application.State.PendingChoices);
    var card = Assert.Single(application.State.Players["player-1"].Zones.Hand);
    Assert.Equal("card-discovered-1", card.CardInstanceId);
    Assert.Equal("card:arcane-b", card.CardId);
    Assert.Equal("card:arcane-b:v1", card.CardVersion);
    Assert.Equal(CardType.SpellCard, card.CardType);
  }

  private ActionExecutionContext CreateContext(IGameState state, string commandKind)
  {
    return CreateContext(state, new NoOpCommand(commandKind));
  }

  private ActionExecutionContext CreateContext(IGameState state, IGameCommand command)
  {
    return new ActionExecutionContext(
      state,
      command,
      state.Revision + 1,
      _hasher.HashGameplayState(state));
  }

  private static InMemoryGameState CreateState(
    PlayerZones zones,
    string? activePlayerId = "player-1",
    int turnNumber = 0,
    string phase = "TURN_START",
    int mana = 0,
    PlayerState? player2 = null,
    IReadOnlyList<PlayerModifier>? modifiers = null,
    IReadOnlyDictionary<string, PendingChoice>? pendingChoices = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Mana = mana,
      MaxMana = mana,
      Modifiers = modifiers ?? [],
      Zones = zones
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player,
        ["player-2"] = player2 ?? PlayerState.Empty("player-2")
      },
      activePlayerId: activePlayerId,
      turnNumber: turnNumber,
      phase: phase,
      pendingChoices: pendingChoices);
  }

  private static CardInstance CreateCard(string cardInstanceId)
  {
    return new CardInstance(
      cardInstanceId,
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
  }

  private static PlayerModifier CreateCostModifier(
    string modifierId,
    int amount,
    string duration = PlayerModifierDurations.NextPlay) =>
    new(
      modifierId,
      PlayerModifierTypes.CostModifier,
      amount,
      duration,
      Priority: 0,
      SourceRef: "test");

  private static EntityInstance CreateEntity(
    string entityInstanceId,
    int damage,
    string controllerPlayerId = "player-1",
    IReadOnlyList<string>? keywords = null)
  {
    return new EntityInstance(
      entityInstanceId,
      "card-instance-1",
      controllerPlayerId,
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: damage,
      Keywords: keywords);
  }

  private static EntityInstance CreateHero(string entityInstanceId, string playerId, int damage)
  {
    return new EntityInstance(
      entityInstanceId,
      $"hero-card:{playerId}",
      playerId,
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      damage);
  }
}
