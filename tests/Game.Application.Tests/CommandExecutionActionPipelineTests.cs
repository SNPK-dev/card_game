using Game.Application.Execution;
using Game.Application.Replay;
using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Effects;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Replacements;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class CommandExecutionActionPipelineTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void CommandExecutionUsesActionExecutorBatchPath()
  {
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(new InMemoryGameState(), new NoOpCommand("draw"));

    Assert.Equal(1, result.State.Revision);
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal("batch:1:draw", mutator.LastBatch?.BatchId);
    Assert.Equal(CommittedEventType.ModifierApplied, result.Events.Single().EventType);
    Assert.Equal(1, result.Events.Single().EventSeq);
  }

  [Fact]
  public void CommandExecutionRejectsMissingActionBeforeStateMutation()
  {
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(
      mutator,
      new DerivedEventLog(),
      new ActionExecutorRegistry([new CommandCounterActionExecutor()]),
      new FixedActionResolver("MISSING_ACTION"),
      _hasher);

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(new InMemoryGameState(), new NoOpCommand("draw")));
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(0, mutator.BatchApplyCount);
  }

  [Fact]
  public void CommandExecutionPreservesDeterministicReplayHash()
  {
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());
    var first = executor.Execute(new InMemoryGameState(), new NoOpCommand("draw")).State;
    var second = executor.Execute(new InMemoryGameState(), new NoOpCommand("draw")).State;

    Assert.Equal(_hasher.HashGameplayState(first), _hasher.HashGameplayState(second));
  }

  [Fact]
  public void PlayCardCommandExecutionUsesActionBatchPath()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        "entity-1",
        Attack: 2,
        Health: 3));

    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal("batch:1:play-card:card-instance-1", mutator.LastBatch?.BatchId);
    Assert.Empty(result.State.Players["player-1"].Zones.Hand);
    Assert.Single(result.State.Players["player-1"].Zones.SetAside);
    Assert.Single(result.State.Players["player-1"].Zones.Board);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.MinionSummoned, CommittedEventType.ModifierApplied],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlaySpellCardCommandMovesCardToGraveyardWithoutSummoningEntity()
  {
    var card = CreateCard("spell-instance-1", CardType.SpellCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId));

    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Empty(result.State.Players["player-1"].Zones.Hand);
    Assert.Single(result.State.Players["player-1"].Zones.Graveyard);
    Assert.Empty(result.State.Players["player-1"].Zones.SetAside);
    Assert.Empty(result.State.Players["player-1"].Zones.Board);
    Assert.Equal(
      [CommittedEventType.CardMovedZone],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlaySpellCardCommandAppliesBoardAttackModifierEffect()
  {
    var spell = CreateCard("spell-board-buff", CardType.SpellCard);
    var first = CreateEntity("entity-board-a", "player-1", attack: 1, health: 3);
    var second = CreateEntity("entity-board-b", "player-1", attack: 2, health: 4);
    var state = CreateState(
      new PlayerZones(hand: [spell], board: [first, second]),
      mana: 1);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateBoardAttackBuffSpellCardVersion(spell.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));

    Assert.Empty(result.State.Players["player-1"].Zones.Hand);
    Assert.Single(result.State.Players["player-1"].Zones.Graveyard);
    Assert.Equal(0, result.State.Players["player-1"].Mana);
    Assert.Equal(
      [3, 4],
      result.State.Players["player-1"].Zones.Board
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => entity.Attack));
    Assert.Equal(
      [
        CommittedEventType.ManaChanged,
        CommittedEventType.CardMovedZone,
        CommittedEventType.ModifierApplied,
        CommittedEventType.ModifierApplied
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlaySpellCardCommandAppliesBoardKeywordModifierEffect()
  {
    var spell = CreateCard("spell-board-taunt", CardType.SpellCard);
    var buffed = CreateEntity("entity-board-a", "player-1", attack: 1, health: 3);
    var opponent = CreateEntity("entity-opponent", "player-2", attack: 2, health: 3);
    var player1Hero = CreateEntity("hero-player-1", "player-1", attack: 0, health: 30, EntityType.Hero);
    var player2Hero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, EntityType.Hero);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = player1Hero,
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [spell], board: [buffed])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = player2Hero,
          Zones = new PlayerZones(board: [opponent])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateBoardKeywordBuffSpellCardVersion(spell.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));

    Assert.Equal(["TAUNT"], Assert.Single(result.State.Players["player-1"].Zones.Board).Keywords);
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.CardMovedZone, CommittedEventType.ModifierApplied],
      result.Events.Select(committed => committed.EventType));
    var attackState = new InMemoryGameState(
      result.State.Revision,
      result.State.Counters,
      result.State.Players,
      result.State.PresentationMetadata,
      activePlayerId: "player-2",
      result.State.TurnNumber,
      result.State.Phase,
      result.State.TerminalState,
      result.State.PendingChoices);
    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        attackState,
        new AttackCommand("player-2", opponent.EntityInstanceId, "player-1", player1Hero.EntityInstanceId),
        firstEventSeq: result.Events.Count + 1));
  }

  [Fact]
  public void PlaySpellCardCommandAppliesSourceOrderDamageDistributionEffect()
  {
    var spell = CreateCard("spell-splash-volley", CardType.SpellCard);
    var first = CreateEntity("entity-opponent-a", "player-2", attack: 1, health: 5);
    var second = CreateEntity("entity-opponent-b", "player-2", attack: 1, health: 5);
    var state = CreateState(
      new PlayerZones(hand: [spell]),
      mana: 1,
      secondPlayerZones: new PlayerZones(board: [second, first]));
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateDamageDistributionSpellCardVersion(spell.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));

    Assert.Equal(2, result.State.Players["player-2"].Zones.Board
      .Single(entity => entity.EntityInstanceId == first.EntityInstanceId)
      .Damage);
    Assert.Equal(1, result.State.Players["player-2"].Zones.Board
      .Single(entity => entity.EntityInstanceId == second.EntityInstanceId)
      .Damage);
    Assert.Equal(
      [
        CommittedEventType.ManaChanged,
        CommittedEventType.CardMovedZone,
        CommittedEventType.EntityDamaged,
        CommittedEventType.EntityDamaged,
        CommittedEventType.EntityDamaged
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayMinionCardCommandAppliesFirstClassAuraEffect()
  {
    var auraCard = CreateCard("minion-aura-source", CardType.MinionCard);
    var state = CreateState(
      new PlayerZones(hand: [auraCard]),
      mana: 2);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateApplyAuraMinionCardVersion(auraCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        auraCard.CardInstanceId,
        "entity-aura-source",
        Attack: 1,
        Health: 3,
        ManaCost: 2));

    var aura = Assert.Single(result.State.Players["player-1"].Auras!);
    Assert.Equal("entity-aura-source", aura.SourceEntityInstanceId);
    Assert.Equal(auraCard.CardInstanceId, aura.SourceCardInstanceId);
    Assert.Equal(
      [
        CommittedEventType.ManaChanged,
        CommittedEventType.CardMovedZone,
        CommittedEventType.MinionSummoned,
        CommittedEventType.ModifierApplied,
        CommittedEventType.AuraRefreshed
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlaySpellCardCommandAppliesFirstClassReplacementEffect()
  {
    var spell = CreateCard("spell-replace-damage", CardType.SpellCard);
    var attacker = CreateEntity("entity-opponent-attacker", "player-2", attack: 3, health: 3);
    var player1Hero = CreateEntity("hero-player-1", "player-1", attack: 0, health: 30, EntityType.Hero);
    var player2Hero = CreateEntity("hero-player-2", "player-2", attack: 0, health: 30, EntityType.Hero);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = player1Hero,
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [spell])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = player2Hero,
          Zones = new PlayerZones(board: [attacker])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateReplaceEventSpellCardVersion(spell.CardVersion)]);

    var replaced = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));
    var attackState = new InMemoryGameState(
      replaced.State.Revision,
      replaced.State.Counters,
      replaced.State.Players,
      replaced.State.PresentationMetadata,
      activePlayerId: "player-2",
      replaced.State.TurnNumber,
      replaced.State.Phase,
      replaced.State.TerminalState,
      replaced.State.PendingChoices);
    var attacked = executor.Execute(
      attackState,
      new AttackCommand("player-2", attacker.EntityInstanceId, "player-1", player1Hero.EntityInstanceId),
      firstEventSeq: replaced.Events.Count + 1);

    Assert.Equal(0, replaced.State.Players["player-1"].Hero!.Damage);
    Assert.Equal(0, attacked.State.Players["player-1"].Hero!.Damage);
    Assert.Empty(attacked.State.Players["player-1"].Modifiers);
    Assert.Contains(replaced.Events, @event => @event.EventType == CommittedEventType.ModifierApplied);
    Assert.Contains(attacked.Events, @event =>
      @event.Category == EventCategory.Proposed
      && @event.EventType == CommittedEventType.DamageWouldBeDealt
      && @event.TargetRefs.Any(target => target.RefId == player1Hero.EntityInstanceId));
    Assert.Contains(attacked.Events, @event =>
      @event.Category == EventCategory.Resolver
      && @event.EventType == CommittedEventType.ReplacementResolved
      && @event.Payload.Any(payload => payload.Key == "result" && payload.Value.StringValue == ReplacementDecisionResult.Cancelled.ToString()));
    Assert.DoesNotContain(attacked.Events, @event => @event.EventType == CommittedEventType.EntityDamaged
      && @event.TargetRefs.Any(target => target.RefId == player1Hero.EntityInstanceId));
  }

  [Fact]
  public void PlaySpellCardCommandSchedulesSelfExtraTurnConsumedByEndTurn()
  {
    var spell = CreateCard("spell-extra-turn", CardType.SpellCard);
    var drawCard = CreateCard("extra-turn-draw", CardType.MinionCard);
    var state = CreateState(
      new PlayerZones(deck: [drawCard], hand: [spell]),
      mana: 1,
      turnNumber: 1);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateExtraTurnSpellCardVersion(spell.CardVersion)]);

    var afterSpell = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));
    var afterEndTurn = executor.Execute(
      afterSpell.State,
      new EndTurnCommand("player-1"),
      firstEventSeq: afterSpell.Events.Count + 1);

    Assert.Contains(afterSpell.Events, @event => @event.EventType == CommittedEventType.ExtraTurnScheduled);
    Assert.Contains(afterSpell.State.Counters, counter =>
      counter.Key.StartsWith("extra-turn:queue:", StringComparison.Ordinal) && counter.Value == 1);
    Assert.Equal("player-1", afterEndTurn.State.ActivePlayerId);
    Assert.Equal(2, afterEndTurn.State.TurnNumber);
    Assert.Contains(afterEndTurn.State.Counters, counter =>
      counter.Key.StartsWith("extra-turn:queue:", StringComparison.Ordinal) && counter.Value == 0);
    Assert.Equal(drawCard.CardInstanceId, afterEndTurn.State.Players["player-1"].Zones.Hand.Single().CardInstanceId);
    Assert.Equal(
      [CommittedEventType.TurnStarted, CommittedEventType.CardDrawn],
      afterEndTurn.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void ReplayRegeneratesExtraTurnScheduleFromCanonicalCommandLog()
  {
    var spell = CreateCard("spell-extra-turn", CardType.SpellCard);
    var drawCard = CreateCard("extra-turn-draw", CardType.MinionCard);
    var initialState = CreateState(
      new PlayerZones(deck: [drawCard], hand: [spell]),
      mana: 1,
      turnNumber: 1);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["manaCost"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = spell.CardInstanceId
        }),
      new DeterministicCommandPayload(
        2,
        "player-1",
        EndTurnCommand.CommandKind)
    ]);
    var replayer = new CommandLogReplayer(
      new StateMutator(),
      new DerivedEventLog(),
      _hasher,
      [CreateExtraTurnSpellCardVersion(spell.CardVersion)]);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Equal("player-1", result.State.ActivePlayerId);
    Assert.Equal(2, result.State.TurnNumber);
    Assert.Equal(drawCard.CardInstanceId, result.State.Players["player-1"].Zones.Hand.Single().CardInstanceId);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ExtraTurnScheduled);
  }

  [Fact]
  public void PlaySpellCardCommandAppliesHandZoneModifierEffect()
  {
    var spell = CreateCard("spell-hand-zone", CardType.SpellCard);
    var handA = CreateCard("card-hand-b", CardType.SpellCard);
    var handB = CreateCard("card-hand-a", CardType.SpellCard);
    var state = CreateState(
      new PlayerZones(hand: [spell, handA, handB]),
      mana: 1);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateHandZoneModifierSpellCardVersion(spell.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        spell.CardInstanceId,
        ManaCost: 1));

    Assert.Empty(result.State.Players["player-1"].Zones.Hand);
    Assert.Equal(
      [spell.CardInstanceId, "card-hand-a", "card-hand-b"],
      result.State.Players["player-1"].Zones.Graveyard.Select(card => card.CardInstanceId));
    Assert.Equal(
      [
        CommittedEventType.ManaChanged,
        CommittedEventType.CardMovedZone,
        CommittedEventType.CardMovedZone,
        CommittedEventType.CardMovedZone
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlaySpellCardCommandRejectsEntityPayload()
  {
    var card = CreateCard("spell-instance-1", CardType.SpellCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        state,
        new PlayCardCommand(
          "player-1",
          card.CardInstanceId,
          "entity-spell",
          Attack: 0,
          Health: 1)));
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(0, mutator.BatchApplyCount);
  }

  [Fact]
  public void SecretCardSetsHiddenStateAndRevealsOnOpponentSummonWithReplay()
  {
    var secretCard = CreateCard("secret-card", CardType.SpellCard);
    var opponentCard = new CardInstance(
      "opponent-minion-card",
      "card:opponent-minion",
      "opponent-minion-v1",
      "player-2",
      CardType.MinionCard);
    var drawCard = new CardInstance(
      "secret-draw-card",
      "card:draw",
      "draw-v1",
      "player-1",
      CardType.SpellCard);
    var state = CreateState(
      new PlayerZones(deck: [drawCard], hand: [secretCard]),
      mana: 10,
      secondPlayerZones: new PlayerZones(hand: [opponentCard]));
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateSecretDrawCardVersion(secretCard.CardVersion)]);

    var afterSecret = executor.Execute(
      state,
      new PlayCardCommand("player-1", secretCard.CardInstanceId, ManaCost: 0));

    Assert.Empty(afterSecret.State.Players["player-1"].Zones.Graveyard);
    Assert.Single(afterSecret.State.Players["player-1"].Zones.SecretZone);
    Assert.Single(afterSecret.State.Players["player-1"].Secrets ?? []);

    var player2Ready = ((InMemoryGameState)afterSecret.State).WithPlayer(
      afterSecret.State.Players["player-2"] with
      {
        Mana = 10,
        MaxMana = 10
      });
    var nextState = new InMemoryGameState(
      player2Ready.Revision,
      player2Ready.Counters,
      player2Ready.Players,
      player2Ready.PresentationMetadata,
      activePlayerId: "player-2",
      player2Ready.TurnNumber,
      "TURN_START",
      player2Ready.TerminalState,
      player2Ready.PendingChoices);

    var revealed = executor.Execute(
      nextState,
      new PlayCardCommand(
        "player-2",
        opponentCard.CardInstanceId,
        "opponent-minion",
        Attack: 1,
        Health: 1),
      firstEventSeq: afterSecret.Events.Count + 1);

    Assert.Contains(revealed.Events, @event => @event.EventType == CommittedEventType.SecretRevealed);
    Assert.Contains(revealed.Events, @event => @event.EventType == CommittedEventType.CardDrawn);
    Assert.Empty(revealed.State.Players["player-1"].Zones.SecretZone);
    Assert.Single(revealed.State.Players["player-1"].Zones.Hand);
    Assert.True(Assert.Single(revealed.State.Players["player-1"].Secrets ?? []).IsRevealed);
  }

  [Fact]
  public void AuraRefreshTracksActiveBoardSourcesAndClearsMissingSources()
  {
    var sourceCard = CreateCard("aura-source-card");
    var sourceEntity = new EntityInstance(
      "aura-source",
      sourceCard.CardInstanceId,
      "player-1",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var state = CreateState(new PlayerZones(setAside: [sourceCard], board: [sourceEntity]));
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateAuraCardVersion(sourceCard.CardVersion)]);

    var refreshed = executor.Execute(state, new NoOpCommand("aura-refresh"));

    Assert.Contains(refreshed.Events, @event => @event.EventType == CommittedEventType.AuraRefreshed);
    Assert.Single(refreshed.State.Players["player-1"].Auras ?? []);

    var staleAuraState = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      player1Auras: refreshed.State.Players["player-1"].Auras);

    var cleared = executor.Execute(staleAuraState, new NoOpCommand("aura-clear"));

    Assert.Contains(cleared.Events, @event => @event.EventType == CommittedEventType.AuraRefreshed);
    Assert.Empty(cleared.State.Players["player-1"].Auras ?? []);
  }

  [Fact]
  public void PlayWeaponCardCommandEquipsWeaponWithoutBoardEntity()
  {
    var card = CreateCard("weapon-instance-1", CardType.WeaponCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        "weapon:weapon-instance-1",
        Attack: 3,
        Durability: 2,
        Keywords: ["LIFESTEAL"]));

    var player = result.State.Players["player-1"];
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Empty(player.Zones.Hand);
    Assert.Single(player.Zones.SetAside);
    Assert.Empty(player.Zones.Board);
    Assert.NotNull(player.Weapon);
    Assert.Equal(EntityType.Weapon, player.Weapon.EntityType);
    Assert.Equal("weapon:weapon-instance-1", player.Weapon.EntityInstanceId);
    Assert.Equal(3, player.Weapon.Attack);
    Assert.Equal(2, player.Weapon.Health);
    Assert.Equal(["LIFESTEAL"], player.Weapon.Keywords);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.WeaponEquipped],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayWeaponCardCommandReplacesOccupiedWeaponSlot()
  {
    var card = CreateCard("weapon-instance-1", CardType.WeaponCard);
    var existingWeaponCard = new CardInstance("card:weapon-existing", "card:old-weapon", "old-weapon-v1", "player-1", CardType.WeaponCard);
    var existingWeapon = CreateEntity("weapon-existing", "player-1", attack: 1, health: 2, entityType: EntityType.Weapon);
    var state = CreateState(new PlayerZones(hand: [card], setAside: [existingWeaponCard]), player1Weapon: existingWeapon);
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        "weapon:weapon-instance-1",
        Attack: 3,
        Durability: 2));

    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal("weapon:weapon-instance-1", result.State.Players["player-1"].Weapon?.EntityInstanceId);
    Assert.Contains(result.State.Players["player-1"].Zones.Graveyard, graveyardCard =>
      graveyardCard.CardInstanceId == existingWeaponCard.CardInstanceId);
    Assert.Contains(result.State.Players["player-1"].Zones.SetAside, setAsideCard =>
      setAsideCard.CardInstanceId == card.CardInstanceId);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.EntityDestroyed, CommittedEventType.WeaponEquipped],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void ReplayRegeneratesWeaponReplacementFromCanonicalCommandLog()
  {
    var card = CreateCard("weapon-instance-1", CardType.WeaponCard);
    var existingWeaponCard = new CardInstance("card:weapon-existing", "card:old-weapon", "old-weapon-v1", "player-1", CardType.WeaponCard);
    var existingWeapon = CreateEntity("weapon-existing", "player-1", attack: 1, health: 2, entityType: EntityType.Weapon);
    var initialState = CreateState(
      new PlayerZones(hand: [card], setAside: [existingWeaponCard]),
      player1Weapon: existingWeapon);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 3,
          ["durability"] = 2
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "weapon:weapon-instance-1"
        })
    ]);
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Equal("weapon:weapon-instance-1", result.State.Players["player-1"].Weapon?.EntityInstanceId);
    Assert.Contains(result.State.Players["player-1"].Zones.Graveyard, graveyardCard =>
      graveyardCard.CardInstanceId == existingWeaponCard.CardInstanceId);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.EntityDestroyed, CommittedEventType.WeaponEquipped],
      result.DerivedEvents.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayLocationCardCommandCreatesLocationWithoutBoardEntity()
  {
    var card = CreateCard("location-instance-1", CardType.LocationCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        "location:location-instance-1",
        Durability: 3));

    var player = result.State.Players["player-1"];
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Empty(player.Zones.Hand);
    Assert.Single(player.Zones.SetAside);
    Assert.Empty(player.Zones.Board);
    Assert.Null(player.Weapon);
    Assert.NotNull(player.Location);
    Assert.Equal(EntityType.Location, player.Location.EntityType);
    Assert.Equal("location:location-instance-1", player.Location.EntityInstanceId);
    Assert.Equal(0, player.Location.Attack);
    Assert.Equal(3, player.Location.Health);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.LocationPlayed],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayLocationCardCommandReplacesOccupiedLocationSlot()
  {
    var card = CreateCard("location-instance-1", CardType.LocationCard);
    var existingLocationCard = new CardInstance("card:location-existing", "card:old-location", "old-location-v1", "player-1", CardType.LocationCard);
    var existingLocation = CreateEntity("location-existing", "player-1", attack: 0, health: 3, entityType: EntityType.Location);
    var state = CreateState(new PlayerZones(hand: [card], setAside: [existingLocationCard]), player1Location: existingLocation);
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        "location:location-instance-1",
        Durability: 3));

    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal("location:location-instance-1", result.State.Players["player-1"].Location?.EntityInstanceId);
    Assert.Contains(result.State.Players["player-1"].Zones.Graveyard, graveyardCard =>
      graveyardCard.CardInstanceId == existingLocationCard.CardInstanceId);
    Assert.Contains(result.State.Players["player-1"].Zones.SetAside, setAsideCard =>
      setAsideCard.CardInstanceId == card.CardInstanceId);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.EntityDestroyed, CommittedEventType.LocationPlayed],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayHeroCardCommandGainsArmorAndReplacesHeroPower()
  {
    var card = CreateCard("hero-card-instance-1", CardType.HeroCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        ManaCost: 0,
        ArmorGain: 5,
        HeroPowerCost: 2,
        HeroPowerActivationTiming: "MAIN_PHASE"));

    var player = result.State.Players["player-1"];
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Empty(player.Zones.Hand);
    Assert.Single(player.Zones.SetAside);
    Assert.Equal(5, player.Armor);
    Assert.NotNull(player.HeroPower);
    Assert.Equal(card.CardInstanceId, player.HeroPower.SourceCardInstanceId);
    Assert.Equal(2, player.HeroPower.Cost);
    Assert.Equal("MAIN_PHASE", player.HeroPower.ActivationTiming);
    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.HeroCardPlayed],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void PlayHeroCardCommandRejectsMissingHeroPowerTimingWhenReplacingPower()
  {
    var card = CreateCard("hero-card-instance-1", CardType.HeroCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        state,
        new PlayCardCommand(
          "player-1",
          card.CardInstanceId,
          ArmorGain: 5,
          HeroPowerCost: 2)));
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(0, mutator.BatchApplyCount);
  }

  [Fact]
  public void PlayHeroPowerCardCommandReplacesActiveHeroPower()
  {
    var card = CreateCard("hero-power-card-instance-1", CardType.HeroPowerCard);
    var state = CreateState(new PlayerZones(hand: [card]), mana: 3);
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        card.CardInstanceId,
        ManaCost: 1,
        HeroPowerCost: 1,
        HeroPowerActivationTiming: "MAIN_PHASE"));

    var player = result.State.Players["player-1"];
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal(2, player.Mana);
    Assert.Empty(player.Zones.Hand);
    Assert.Single(player.Zones.SetAside);
    Assert.NotNull(player.HeroPower);
    Assert.Equal(card.CardInstanceId, player.HeroPower.SourceCardInstanceId);
    Assert.Equal(1, player.HeroPower.Cost);
    Assert.Equal("MAIN_PHASE", player.HeroPower.ActivationTiming);
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.CardMovedZone, CommittedEventType.HeroPowerReplaced],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void ReplayRegeneratesHeroPowerCardReplacementFromCanonicalCommandLog()
  {
    var card = CreateCard("hero-power-card-instance-1", CardType.HeroPowerCard);
    var initialState = CreateState(new PlayerZones(hand: [card]), mana: 3);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["heroPowerCost"] = 1,
          ["manaCost"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["heroPowerActivationTiming"] = "MAIN_PHASE"
        })
    ]);
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Equal(2, result.State.Players["player-1"].Mana);
    Assert.Equal(card.CardInstanceId, result.State.Players["player-1"].HeroPower?.SourceCardInstanceId);
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.CardMovedZone, CommittedEventType.HeroPowerReplaced],
      result.DerivedEvents.Select(committed => committed.EventType));
  }

  [Fact]
  public void UseHeroPowerCommandPaysManaMarksUsedAndAppliesEffect()
  {
    var sourceCard = CreateCard("hero-card-instance-1", CardType.HeroCard);
    var opponentHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = opponentHero
      });
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateHeroPowerSourceCardVersion(sourceCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new UseHeroPowerCommand(
        "player-1",
        sourceCard.CardInstanceId,
        2,
        "player-2",
        opponentHero.EntityInstanceId));

    Assert.Equal(0, result.State.Players["player-1"].Mana);
    Assert.Equal(1, result.State.Counters[
      UseHeroPowerActionExecutor.ActivationCounterKey(0, "player-1", sourceCard.CardInstanceId)]);
    Assert.Equal(1, result.State.Players["player-2"].Hero?.Damage);
    Assert.Equal(
      [CommittedEventType.ManaChanged, CommittedEventType.HeroPowerUsed, CommittedEventType.EntityDamaged],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void UseHeroPowerCommandRejectsSecondUseInSameTurn()
  {
    var sourceCard = CreateCard("hero-card-instance-1", CardType.HeroCard);
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard]),
      mana: 2,
      heroPower: new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"),
      counters: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        [UseHeroPowerActionExecutor.ActivationCounterKey(0, "player-1", sourceCard.CardInstanceId)] = 1
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        state,
        new UseHeroPowerCommand("player-1", sourceCard.CardInstanceId, 2)));
  }

  [Fact]
  public void AttackCommandWithWeaponDamagesTargetAndConsumesDurability()
  {
    var weaponCard = CreateCard("weapon-instance-1", CardType.WeaponCard);
    var weapon = new EntityInstance(
      "weapon:weapon-instance-1",
      weaponCard.CardInstanceId,
      "player-1",
      EntityType.Weapon,
      Attack: 3,
      Health: 2,
      Damage: 0);
    var defendingHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Weapon = weapon,
          Zones = new PlayerZones(setAside: [weaponCard])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = defendingHero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", weapon.EntityInstanceId, "player-2", defendingHero.EntityInstanceId));

    Assert.Equal(3, result.State.Players["player-2"].Hero?.Damage);
    Assert.NotNull(result.State.Players["player-1"].Weapon);
    Assert.Equal(1, result.State.Players["player-1"].Weapon?.Damage);
    Assert.Equal(1, result.State.Counters[
      AttackActionExecutor.AttackCounterKey(1, "player-1", weapon.EntityInstanceId)]);
    Assert.Equal(
      [CommittedEventType.EntityDamaged, CommittedEventType.EntityDamaged, CommittedEventType.ModifierApplied],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void AttackCommandWithLastWeaponDurabilityDestroysWeaponAndMovesCardToGraveyard()
  {
    var weaponCard = CreateCard("weapon-instance-1", CardType.WeaponCard);
    var weapon = new EntityInstance(
      "weapon:weapon-instance-1",
      weaponCard.CardInstanceId,
      "player-1",
      EntityType.Weapon,
      Attack: 2,
      Health: 1,
      Damage: 0);
    var defendingHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Weapon = weapon,
          Zones = new PlayerZones(setAside: [weaponCard])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = defendingHero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", weapon.EntityInstanceId, "player-2", defendingHero.EntityInstanceId));

    Assert.Null(result.State.Players["player-1"].Weapon);
    Assert.Empty(result.State.Players["player-1"].Zones.SetAside);
    Assert.Contains(result.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, weaponCard.CardInstanceId));
    Assert.Equal(
      [
        CommittedEventType.EntityDamaged,
        CommittedEventType.EntityDamaged,
        CommittedEventType.EntityDestroyed,
        CommittedEventType.ModifierApplied
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void AttackCommandConsumesDivineShieldInsteadOfDamagingTarget()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defender = CreateEntity("entity-defender", "player-2", attack: 1, health: 3, keywords: ["DIVINE_SHIELD"]);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = CreateHero("hero-player-2", "player-2", damage: 0),
          Zones = new PlayerZones(board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));

    var nextDefender = Assert.Single(result.State.Players["player-2"].Zones.Board);
    Assert.Equal(0, nextDefender.Damage);
    Assert.Null(nextDefender.Keywords);
    Assert.Equal(1, result.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Equal(
      [
        CommittedEventType.DamageWouldBeDealt,
        CommittedEventType.ReplacementResolved,
        CommittedEventType.ModifierExpired,
        CommittedEventType.EntityDamaged,
        CommittedEventType.ModifierApplied
      ],
      result.Events.Select(committed => committed.EventType));
    Assert.Equal(EventCategory.Proposed, result.Events[0].Category);
    Assert.Equal(EventCategory.Resolver, result.Events[1].Category);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.ModifierExpired
      && @event.Payload.Any(payload => payload.Key == "keyword" && payload.Value.StringValue == "DIVINE_SHIELD"));
  }

  [Fact]
  public void ActivateLocationCommandMarksUsedConsumesDurabilityAndAppliesEffect()
  {
    var locationCard = CreateCard("location-instance-1", CardType.LocationCard);
    var location = new EntityInstance(
      "location:location-instance-1",
      locationCard.CardInstanceId,
      "player-1",
      EntityType.Location,
      Attack: 0,
      Health: 2,
      Damage: 0);
    var defendingHero = CreateHero("hero-player-2", "player-2", damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Location = location,
          Zones = new PlayerZones(setAside: [locationCard])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = defendingHero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateLocationAbilityCardVersion(locationCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new ActivateLocationCommand(
        "player-1",
        location.EntityInstanceId,
        locationCard.CardInstanceId,
        "player-2",
        defendingHero.EntityInstanceId));

    Assert.Equal(1, result.State.Counters[
      ActivateLocationActionExecutor.ActivationCounterKey(1, "player-1", location.EntityInstanceId)]);
    Assert.Equal(1, result.State.Players["player-1"].Location?.Damage);
    Assert.Equal(2, result.State.Players["player-2"].Hero?.Damage);
    Assert.Equal(
      [CommittedEventType.LocationActivated, CommittedEventType.EntityDamaged, CommittedEventType.EntityDamaged],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void ActivateLocationCommandRejectsSecondUseInSameTurn()
  {
    var locationCard = CreateCard("location-instance-1", CardType.LocationCard);
    var location = new EntityInstance(
      "location:location-instance-1",
      locationCard.CardInstanceId,
      "player-1",
      EntityType.Location,
      Attack: 0,
      Health: 2,
      Damage: 0);
    var state = CreateState(
      new PlayerZones(setAside: [locationCard]),
      player1Location: location,
      counters: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        [ActivateLocationActionExecutor.ActivationCounterKey(0, "player-1", location.EntityInstanceId)] = 1
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        state,
        new ActivateLocationCommand("player-1", location.EntityInstanceId, locationCard.CardInstanceId)));
  }



  [Fact]
  public void PlayCardCommandRejectsMissingHandCardBeforeStateMutation()
  {
    var card = CreateCard("card-instance-1");
    var state = CreateState(new PlayerZones(deck: [card]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(
        state,
        new PlayCardCommand(
          "player-1",
          card.CardInstanceId,
          "entity-1",
          Attack: 2,
          Health: 3)));
    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(0, mutator.BatchApplyCount);
    Assert.Single(state.Players["player-1"].Zones.Deck);
  }

  [Fact]
  public void ResolveChoiceCommandExecutionMaterializesSelectedCard()
  {
    var choice = new PendingChoice(
      "choice-spell",
      "player-1",
      "DISCOVER",
      "PUBLIC",
      "HAND",
      "effect:test",
      [new ChoiceOption("choice-spell:option:0001", "CARD", "card:arcane-a")]);
    var state = CreateState(
      PlayerZones.Empty,
      pendingChoices: new SortedDictionary<string, PendingChoice>(StringComparer.Ordinal)
      {
        [choice.ChoiceId] = choice
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new ResolveChoiceCommand(
        "player-1",
        "choice-spell",
        "choice-spell:option:0001",
        "card-discovered-1",
        "card:arcane-a:v1",
        CardType.SpellCard));

    Assert.Empty(result.State.PendingChoices);
    Assert.Equal("card:arcane-a", Assert.Single(result.State.Players["player-1"].Zones.Hand).CardId);
    Assert.Equal(
      [CommittedEventType.ChoiceResolved, CommittedEventType.CardMovedZone],
      result.Events.Select(@event => @event.EventType));
  }

  [Fact]
  public void EquivalentPlayCardCommandsProduceSameGameplayStateHash()
  {
    var card = CreateCard("card-instance-1");
    var firstState = CreateState(new PlayerZones(hand: [card]));
    var secondState = CreateState(new PlayerZones(hand: [card]));
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var first = executor.Execute(
      firstState,
      new PlayCardCommand("player-1", card.CardInstanceId, "entity-1", 2, 3)).State;
    var second = executor.Execute(
      secondState,
      new PlayCardCommand("player-1", card.CardInstanceId, "entity-1", 2, 3)).State;

    Assert.Equal(_hasher.HashGameplayState(first), _hasher.HashGameplayState(second));
  }

  [Fact]
  public void EndTurnCommandExecutionUsesActionBatchPath()
  {
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      secondPlayerZones: new PlayerZones(deck: [CreateCard("player-2-card-1")]));
    var mutator = new RejectDirectCommandMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(state, new EndTurnCommand("player-1"));

    Assert.Equal(0, mutator.DirectCommandApplyCount);
    Assert.Equal(1, mutator.BatchApplyCount);
    Assert.Equal("batch:1:end-turn:player-1", mutator.LastBatch?.BatchId);
    Assert.Equal("player-2", result.State.ActivePlayerId);
    Assert.Equal(1, result.State.TurnNumber);
    Assert.Equal(
      [CommittedEventType.TurnStarted, CommittedEventType.CardDrawn],
      result.Events.Select(committed => committed.EventType));
    Assert.Empty(result.State.Players["player-2"].Zones.Deck);
    Assert.Single(result.State.Players["player-2"].Zones.Hand);
  }

  [Fact]
  public void EndTurnCommandExecutionExpiresRestOfTurnCostModifiers()
  {
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      secondPlayerZones: new PlayerZones(deck: [CreateCard("player-2-card-1")]),
      player1Modifiers:
      [
        new PlayerModifier(
          "modifier-rest-turn",
          PlayerModifierTypes.CostModifier,
          Amount: 2,
          PlayerModifierDurations.RestOfTurn,
          Priority: 0,
          SourceRef: "test")
      ]);
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(state, new EndTurnCommand("player-1"));

    Assert.Empty(result.State.Players["player-1"].Modifiers);
    Assert.Equal(
      [CommittedEventType.ModifierExpired, CommittedEventType.TurnStarted, CommittedEventType.CardDrawn],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void EquivalentEndTurnCommandLogsProduceSameGameplayStateHash()
  {
    var firstState = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 1, phase: "TURN_START");
    var secondState = CreateState(PlayerZones.Empty, activePlayerId: "player-1", turnNumber: 1, phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var first = executor.Execute(firstState, new EndTurnCommand("player-1")).State;
    var second = executor.Execute(secondState, new EndTurnCommand("player-1")).State;

    Assert.Equal(_hasher.HashGameplayState(first), _hasher.HashGameplayState(second));
    Assert.Equal("player-2", first.ActivePlayerId);
  }

  [Fact]
  public void AttackCommandExecutionCanDefeatHeroThroughTerminalMutation()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 2);
    var hero = CreateHero("hero-player-2", "player-2", damage: 28);
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = hero
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", hero.EntityInstanceId));

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", result.State.TerminalState.Reason);
    Assert.Equal("player-1", result.State.TerminalState.WinningPlayerId);
    Assert.Equal(
      [CommittedEventType.EntityDamaged, CommittedEventType.ModifierApplied, CommittedEventType.GameEnded],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void AttackCommandExecutionDestroysDeadMinionsBeforeTerminalEvaluation()
  {
    var sourceCard = CreateCard("card-attacker");
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 2);
    var defender = CreateEntity("entity-defender", "player-2", attack: 2, health: 3);
    attacker = attacker with { SourceCardInstanceId = sourceCard.CardInstanceId };
    var state = CreateState(
      new PlayerZones(setAside: [sourceCard], board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(board: [defender])
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));

    Assert.Empty(result.State.Players["player-1"].Zones.Board);
    Assert.Empty(result.State.Players["player-2"].Zones.Board);
    Assert.Equal(sourceCard.CardInstanceId, result.State.Players["player-1"].Zones.Graveyard.Single().CardInstanceId);
    Assert.Equal(
      [
        CommittedEventType.EntityDamaged,
        CommittedEventType.EntityDamaged,
        CommittedEventType.ModifierApplied,
        CommittedEventType.EntityDestroyed,
        CommittedEventType.EntityDestroyed,
        CommittedEventType.EntityDied,
        CommittedEventType.EntityDied
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void AttackCommandExecutionTriggersDeathrattleAfterSimultaneousDeathCleanup()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defenderCard = new CardInstance(
      "card-defender-deathrattle",
      "card:deathrattle-draw",
      "card_deathrattle_draw_v1",
      "player-2",
      CardType.MinionCard);
    var deckCard = new CardInstance(
      "card-player-2-deck",
      "card:drawn",
      "card_drawn_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var state = CreateState(
      new PlayerZones(board: [attacker]),
      player2: PlayerState.Empty("player-2") with
      {
        Zones = new PlayerZones(
          deck: [deckCard],
          setAside: [defenderCard],
          board: [defender])
      });
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateDeathrattleDrawCardVersion(defenderCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));

    Assert.Empty(result.State.Players["player-2"].Zones.Board);
    Assert.Contains(result.State.Players["player-2"].Zones.Graveyard, card =>
      card.CardInstanceId == defenderCard.CardInstanceId);
    Assert.Contains(result.State.Players["player-2"].Zones.Hand, card =>
      card.CardInstanceId == deckCard.CardInstanceId);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.EntityDied);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.DeathrattleTriggered);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.CardDrawn
      && @event.ParentEventSeq is not null);
  }

  [Fact]
  public void AttackCommandExecutionTurnsDeathProcessingChainLimitIntoEngineFault()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 1, health: 30);
    var chainCards = Enumerable.Range(1, 9)
      .Select(index => new CardInstance(
        $"card-chain-{index}",
        $"card:chain-{index}",
        $"card_chain_{index}_v1",
        index % 2 == 0 ? "player-1" : "player-2",
        CardType.MinionCard))
      .ToArray();
    var chainEntities = chainCards
      .Select((card, index) => new EntityInstance(
        $"entity-chain-{index + 1}",
        card.CardInstanceId,
        card.OwnerPlayerId,
        EntityType.Minion,
        Attack: 0,
        Health: 1,
        Damage: 0,
        Keywords: ["DEATHRATTLE"]))
      .ToArray();
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Zones = new PlayerZones(
            setAside: chainCards.Where(card => card.OwnerPlayerId == "player-1").ToArray(),
            board: [attacker, .. chainEntities.Where(entity => entity.ControllerPlayerId == "player-1")])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = CreateHero("hero-player-2", "player-2", damage: 0),
          Zones = new PlayerZones(
            setAside: chainCards.Where(card => card.OwnerPlayerId == "player-2").ToArray(),
            board: chainEntities.Where(entity => entity.ControllerPlayerId == "player-2").ToArray())
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalog = chainCards
      .Select((card, index) => CreateChainDeathrattleCardVersion(
        card.CardVersion,
        nextTargetPlayerId: index + 1 < chainCards.Length ? chainCards[index + 1].OwnerPlayerId : null,
        nextTargetEntityId: index + 1 < chainCards.Length ? $"entity-chain-{index + 2}" : null))
      .ToArray();
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      catalog,
      effectActionCompiler: new ChainDamageEffectCompiler(_hasher));

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", "entity-chain-1"));

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("ENGINE_FAULT", result.State.TerminalState.Reason);
    Assert.Contains(
      "Death processing exceeded the maximum deterministic pass count of 8",
      result.State.TerminalState.Message,
      StringComparison.Ordinal);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.DeathrattleTriggered);
    var faultEvent = Assert.Single(result.Events, @event => @event.EventType == CommittedEventType.EngineFaulted);
    Assert.Contains(faultEvent.Payload, entry =>
      entry.Key == "failedCommandKind"
      && entry.Value.StringValue == AttackCommand.CommandKind);
  }

  [Fact]
  public void AttackCommandExecutionResolvesTargetedDeathrattleAgainstOpponentHero()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defenderCard = new CardInstance(
      "card-defender-targeted-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-targeted-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var state = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = CreateHero("hero-player-2", "player-2", damage: 0),
          Zones = new PlayerZones(setAside: [defenderCard], board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateTargetedDeathrattleDamageCardVersion(defenderCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));

    Assert.Equal(2, result.State.Players["player-1"].Hero?.Damage);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.DeathrattleTriggered);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-1"));
  }

  [Fact]
  public void AttackCommandExecutionCreatesPendingChoiceForPlayerChosenDeathrattleTarget()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defenderCard = new CardInstance(
      "card-defender-targeted-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-targeted-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var state = CreateDeathrattleTargetChoiceState(attacker, defenderCard, defender);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreatePlayerChosenDeathrattleDamageCardVersion(defenderCard.CardVersion)]);

    var result = executor.Execute(
      state,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));

    var choice = Assert.Single(result.State.PendingChoices.Values);
    Assert.Equal("player-2", choice.PlayerId);
    Assert.Equal("EFFECT_TARGET", choice.ChoiceType);
    Assert.Equal("TARGET", choice.ResultZone);
    Assert.Equal("PUBLIC", choice.VisibilityPolicy);
    var option = Assert.Single(choice.Options);
    Assert.Equal("TARGET", option.ItemKind);
    Assert.Contains("hero-player-1", option.ItemId, StringComparison.Ordinal);
    Assert.Equal(0, result.State.Players["player-1"].Hero?.Damage);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.DeathrattleTriggered);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.ChoiceCreated
      && @event.ParentEventSeq is not null);
  }

  [Fact]
  public void ResolveChoiceCommandExecutionAppliesPlayerChosenDeathrattleTarget()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defenderCard = new CardInstance(
      "card-defender-targeted-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-targeted-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = CreateDeathrattleTargetChoiceState(attacker, defenderCard, defender);
    var catalog = CreatePlayerChosenDeathrattleDamageCardVersion(defenderCard.CardVersion);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [catalog]);
    var attack = executor.Execute(
      initialState,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));
    var choice = Assert.Single(attack.State.PendingChoices.Values);
    var option = Assert.Single(choice.Options);

    var resolve = executor.Execute(
      attack.State,
      new ResolveChoiceCommand(
        "player-2",
        choice.ChoiceId,
        option.OptionId,
        "effect-target-placeholder-card",
        "effect_target_placeholder_v1",
        CardType.SpellCard),
      firstEventSeq: attack.Events.Count + 1);

    Assert.Empty(resolve.State.PendingChoices);
    Assert.Equal(2, resolve.State.Players["player-1"].Hero?.Damage);
    Assert.Equal(
      [CommittedEventType.ChoiceResolved, CommittedEventType.EntityDamaged],
      resolve.Events.Select(@event => @event.EventType));
    Assert.Contains(resolve.Events, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-1"));
  }

  [Fact]
  public void ResolveChoiceCommandExecutionAppliesMultiplePlayerChosenDeathrattleTargets()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 4);
    var secondTarget = CreateEntity("entity-second-target", "player-1", attack: 1, health: 4);
    var defenderCard = new CardInstance(
      "card-defender-multi-target-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_multi_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-multi-target-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = CreateDeathrattleTargetChoiceState(
      attacker,
      defenderCard,
      defender,
      additionalPlayer1Board: [secondTarget]);
    var catalog = CreateDeathrattleDamageCardVersion(
      defenderCard.CardVersion,
      OpponentBoardMinionsPlayerChosenTarget(count: 2));
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [catalog]);
    var attack = executor.Execute(
      initialState,
      new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", defender.EntityInstanceId));
    var choice = Assert.Single(attack.State.PendingChoices.Values);
    Assert.Equal(2, choice.Options.Count);

    var resolve = executor.Execute(
      attack.State,
      new ResolveChoiceCommand(
        "player-2",
        choice.ChoiceId,
        choice.Options[0].OptionId,
        "effect-target-placeholder-card",
        "effect_target_placeholder_v1",
        CardType.SpellCard,
        choice.Options.Select(option => option.OptionId).ToArray()),
      firstEventSeq: attack.Events.Count + 1);

    Assert.Empty(resolve.State.PendingChoices);
    Assert.Contains(resolve.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, attacker.EntityInstanceId)
      && entity.Damage == 3);
    Assert.Contains(resolve.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, secondTarget.EntityInstanceId)
      && entity.Damage == 2);
    Assert.Equal(
      [CommittedEventType.ChoiceResolved, CommittedEventType.EntityDamaged, CommittedEventType.EntityDamaged],
      resolve.Events.Select(@event => @event.EventType));
  }

  [Fact]
  public void ReplayRegeneratesMultiplePlayerChosenDeathrattleTargetsFromCanonicalCommandLog()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 4);
    var secondTarget = CreateEntity("entity-second-target", "player-1", attack: 1, health: 4);
    var defenderCard = new CardInstance(
      "card-defender-multi-target-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_multi_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-multi-target-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = CreateDeathrattleTargetChoiceState(
      attacker,
      defenderCard,
      defender,
      additionalPlayer1Board: [secondTarget]);
    var choiceId = $"choice:deathrattle:{defender.EntityInstanceId}:effect_deathrattle_bolt";
    var firstOptionId = $"{choiceId}:option:0001";
    var secondOptionId = $"{choiceId}:option:0002";
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetEntityInstanceId"] = defender.EntityInstanceId,
          ["targetPlayerId"] = "player-2"
        }),
      new DeterministicCommandPayload(
        2,
        "player-2",
        ResolveChoiceCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = "effect-target-placeholder-card",
          ["cardType"] = "SPELL_CARD",
          ["cardVersion"] = "effect_target_placeholder_v1",
          ["choiceId"] = choiceId,
          ["selectedOptionId"] = firstOptionId,
          ["selectedOptionIds"] = $"{firstOptionId},{secondOptionId}"
        },
        commandCategory: CommandCategory.Choice)
    ]);
    var replayer = new CommandLogReplayer(
      new StateMutator(),
      new DerivedEventLog(),
      _hasher,
      [CreateDeathrattleDamageCardVersion(
        defenderCard.CardVersion,
        OpponentBoardMinionsPlayerChosenTarget(count: 2))]);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Empty(result.State.PendingChoices);
    Assert.Contains(result.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, attacker.EntityInstanceId)
      && entity.Damage == 3);
    Assert.Contains(result.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.EntityInstanceId, secondTarget.EntityInstanceId)
      && entity.Damage == 2);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceCreated);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceResolved);
    Assert.Equal(2, result.DerivedEvents.Count(@event => @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null));
  }

  [Fact]
  public void ReplayRegeneratesPlayerChosenDeathrattleTargetChoiceFromCanonicalCommandLog()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 3);
    var defenderCard = new CardInstance(
      "card-defender-targeted-deathrattle",
      "card:deathrattle-bolt",
      "card_deathrattle_bolt_v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-defender-targeted-deathrattle",
      defenderCard.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = CreateDeathrattleTargetChoiceState(attacker, defenderCard, defender);
    var choiceId = $"choice:deathrattle:{defender.EntityInstanceId}:effect_deathrattle_bolt";
    var optionId = $"{choiceId}:option:0001";
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetEntityInstanceId"] = defender.EntityInstanceId,
          ["targetPlayerId"] = "player-2"
        }),
      new DeterministicCommandPayload(
        2,
        "player-2",
        ResolveChoiceCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = "effect-target-placeholder-card",
          ["cardType"] = "SPELL_CARD",
          ["cardVersion"] = "effect_target_placeholder_v1",
          ["choiceId"] = choiceId,
          ["selectedOptionId"] = optionId
        },
        commandCategory: CommandCategory.Choice)
    ]);
    var replayer = new CommandLogReplayer(
      new StateMutator(),
      new DerivedEventLog(),
      _hasher,
      [CreatePlayerChosenDeathrattleDamageCardVersion(defenderCard.CardVersion)]);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Empty(result.State.PendingChoices);
    Assert.Equal(2, result.State.Players["player-1"].Hero?.Damage);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceCreated);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceResolved);
    Assert.Contains(result.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-1"));
  }

  [Fact]
  public void PlayCardCommandProgressesQuestAndExecutesCompletionReward()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-draw",
      "card_quest_draw_v1",
      "player-1",
      CardType.SpellCard);
    var rewardCard = CreateCard("card-reward-drawn");
    var quest = new PlayerQuest(
      "quest-play-minion",
      questSource.CardInstanceId,
      "player-1",
      "MINION_SUMMONED",
      Progress: 0,
      RequiredProgress: 1,
      IsCompleted: false,
      RewardEffectId: "effect_quest_reward_draw",
      SourceCardVersion: questSource.CardVersion);
    var state = CreateState(
      new PlayerZones(
        deck: [rewardCard],
        hand: [playedCard],
        setAside: [questSource]),
      player1Quests: [quest]);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateQuestRewardDrawCardVersion(questSource.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        playedCard.CardInstanceId,
        "entity-quest-progress",
        Attack: 1,
        Health: 1));

    var updatedQuest = Assert.Single(result.State.Players["player-1"].Quests ?? []);
    Assert.True(updatedQuest.IsCompleted);
    Assert.Equal(1, updatedQuest.Progress);
    Assert.Contains(result.State.Players["player-1"].Zones.Hand, card =>
      card.CardInstanceId == rewardCard.CardInstanceId);
    var progressEvent = Assert.Single(result.Events, @event =>
      @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.CardDrawn
      && @event.ParentEventSeq == progressEvent.EventSeq);
  }

  [Fact]
  public void PlayCardCommandResolvesTargetedQuestRewardAgainstOpponentHero()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-bolt",
      "card_quest_bolt_v1",
      "player-1",
      CardType.SpellCard);
    var quest = new PlayerQuest(
      "quest-play-minion",
      questSource.CardInstanceId,
      "player-1",
      "MINION_SUMMONED",
      Progress: 0,
      RequiredProgress: 1,
      IsCompleted: false,
      RewardEffectId: "effect_quest_reward_bolt",
      SourceCardVersion: questSource.CardVersion);
    var state = CreateState(
      new PlayerZones(hand: [playedCard], setAside: [questSource]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = CreateHero("hero-player-2", "player-2", damage: 0)
      },
      player1Quests: [quest]);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreateTargetedQuestRewardDamageCardVersion(questSource.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        playedCard.CardInstanceId,
        "entity-quest-progress",
        Attack: 1,
        Health: 1));

    Assert.Equal(3, result.State.Players["player-2"].Hero?.Damage);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-2"));
  }

  [Fact]
  public void ReplayRegeneratesTargetedQuestRewardFromCanonicalCommandLog()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-bolt",
      "card_quest_bolt_v1",
      "player-1",
      CardType.SpellCard);
    var quest = new PlayerQuest(
      "quest-play-minion",
      questSource.CardInstanceId,
      "player-1",
      "MINION_SUMMONED",
      Progress: 0,
      RequiredProgress: 1,
      IsCompleted: false,
      RewardEffectId: "effect_quest_reward_bolt",
      SourceCardVersion: questSource.CardVersion);
    var initialState = CreateState(
      new PlayerZones(hand: [playedCard], setAside: [questSource]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = CreateHero("hero-player-2", "player-2", damage: 0)
      },
      player1Quests: [quest]);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = playedCard.CardInstanceId,
          ["entityInstanceId"] = "entity-quest-progress"
        })
    ]);
    var replayer = new CommandLogReplayer(
      new StateMutator(),
      new DerivedEventLog(),
      _hasher,
      [CreateTargetedQuestRewardDamageCardVersion(questSource.CardVersion)]);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Equal(3, result.State.Players["player-2"].Hero?.Damage);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(result.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-2"));
  }

  [Fact]
  public void PlayCardCommandCreatesPendingChoiceForPlayerChosenQuestRewardTarget()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-bolt",
      "card_quest_bolt_v1",
      "player-1",
      CardType.SpellCard);
    var quest = CreateQuestRewardBoltQuest(questSource);
    var state = CreateState(
      new PlayerZones(hand: [playedCard], setAside: [questSource]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = CreateHero("hero-player-2", "player-2", damage: 0)
      },
      player1Quests: [quest]);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [CreatePlayerChosenQuestRewardDamageCardVersion(questSource.CardVersion)]);

    var result = executor.Execute(
      state,
      new PlayCardCommand(
        "player-1",
        playedCard.CardInstanceId,
        "entity-quest-progress",
        Attack: 1,
        Health: 1));

    var choice = Assert.Single(result.State.PendingChoices.Values);
    Assert.Equal("player-1", choice.PlayerId);
    Assert.Equal("EFFECT_TARGET", choice.ChoiceType);
    Assert.Equal("TARGET", choice.ResultZone);
    Assert.StartsWith("effect-choice-v1|QUEST|", choice.SourceRef, StringComparison.Ordinal);
    var option = Assert.Single(choice.Options);
    Assert.Equal("TARGET", option.ItemKind);
    Assert.Contains("hero-player-2", option.ItemId, StringComparison.Ordinal);
    Assert.Equal(0, result.State.Players["player-2"].Hero?.Damage);
    Assert.Contains(result.Events, @event => @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(result.Events, @event =>
      @event.EventType == CommittedEventType.ChoiceCreated
      && @event.Phase == Phase.AfterPlay
      && @event.ParentEventSeq is not null);
  }

  [Fact]
  public void ResolveChoiceCommandExecutionAppliesPlayerChosenQuestRewardTarget()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-bolt",
      "card_quest_bolt_v1",
      "player-1",
      CardType.SpellCard);
    var quest = CreateQuestRewardBoltQuest(questSource);
    var initialState = CreateState(
      new PlayerZones(hand: [playedCard], setAside: [questSource]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = CreateHero("hero-player-2", "player-2", damage: 0)
      },
      player1Quests: [quest]);
    var catalog = CreatePlayerChosenQuestRewardDamageCardVersion(questSource.CardVersion);
    var executor = new DeterministicCommandExecutor(
      new StateMutator(),
      new DerivedEventLog(),
      [catalog]);
    var progress = executor.Execute(
      initialState,
      new PlayCardCommand(
        "player-1",
        playedCard.CardInstanceId,
        "entity-quest-progress",
        Attack: 1,
        Health: 1));
    var choice = Assert.Single(progress.State.PendingChoices.Values);
    var option = Assert.Single(choice.Options);

    var resolve = executor.Execute(
      progress.State,
      new ResolveChoiceCommand(
        "player-1",
        choice.ChoiceId,
        option.OptionId,
        "effect-target-placeholder-card",
        "effect_target_placeholder_v1",
        CardType.SpellCard),
      firstEventSeq: progress.Events.Count + 1);

    Assert.Empty(resolve.State.PendingChoices);
    Assert.Equal(3, resolve.State.Players["player-2"].Hero?.Damage);
    Assert.Equal(
      [CommittedEventType.ChoiceResolved, CommittedEventType.EntityDamaged],
      resolve.Events.Select(@event => @event.EventType));
    Assert.All(resolve.Events, @event => Assert.Equal(Phase.AfterPlay, @event.Phase));
    Assert.Contains(resolve.Events, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.ParentEventSeq is not null
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-2"));
  }

  [Fact]
  public void ReplayRegeneratesPlayerChosenQuestRewardTargetChoiceFromCanonicalCommandLog()
  {
    var playedCard = CreateCard("card-instance-quest-progress");
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-bolt",
      "card_quest_bolt_v1",
      "player-1",
      CardType.SpellCard);
    var quest = CreateQuestRewardBoltQuest(questSource);
    var initialState = CreateState(
      new PlayerZones(hand: [playedCard], setAside: [questSource]),
      player2: PlayerState.Empty("player-2") with
      {
        Hero = CreateHero("hero-player-2", "player-2", damage: 0)
      },
      player1Quests: [quest]);
    var choiceId = $"choice:quest:{quest.QuestId}:effect_quest_reward_bolt";
    var optionId = $"{choiceId}:option:0001";
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = playedCard.CardInstanceId,
          ["entityInstanceId"] = "entity-quest-progress"
        }),
      new DeterministicCommandPayload(
        2,
        "player-1",
        ResolveChoiceCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = "effect-target-placeholder-card",
          ["cardType"] = "SPELL_CARD",
          ["cardVersion"] = "effect_target_placeholder_v1",
          ["choiceId"] = choiceId,
          ["selectedOptionId"] = optionId
        },
        commandCategory: CommandCategory.Choice)
    ]);
    var replayer = new CommandLogReplayer(
      new StateMutator(),
      new DerivedEventLog(),
      _hasher,
      [CreatePlayerChosenQuestRewardDamageCardVersion(questSource.CardVersion)]);

    var result = replayer.Replay(initialState, commandLog);

    Assert.Empty(result.State.PendingChoices);
    Assert.Equal(3, result.State.Players["player-2"].Hero?.Damage);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceCreated);
    Assert.Contains(result.DerivedEvents, @event => @event.EventType == CommittedEventType.ChoiceResolved);
    Assert.Contains(result.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.EntityDamaged
      && @event.TargetRefs.Any(target => target.RefId == "hero-player-2"));
  }

  [Fact]
  public void EndTurnCommandExecutionCanDefeatHeroThroughFatigue()
  {
    var hero = CreateHero("hero-player-2", "player-2", damage: 29);
    var state = CreateState(
      PlayerZones.Empty,
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START",
      player2: PlayerState.Empty("player-2") with
      {
        Hero = hero
      });
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(state, new EndTurnCommand("player-1"));

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("HERO_DEFEATED", result.State.TerminalState.Reason);
    Assert.Equal(1, result.State.Players["player-2"].FatigueCounter);
    Assert.Equal(
      [
        CommittedEventType.TurnStarted,
        CommittedEventType.ModifierApplied,
        CommittedEventType.EntityDamaged,
        CommittedEventType.GameEnded
      ],
      result.Events.Select(committed => committed.EventType));
  }

  [Fact]
  public void AttackCommandExecutionRejectsMinionSummonedThisTurn()
  {
    var attacker = CreateEntity("entity-attacker", "player-1", attack: 3, health: 2);
    var hero = CreateHero("hero-player-2", "player-2", damage: 0);
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
          Hero = hero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(state, new AttackCommand("player-1", attacker.EntityInstanceId, "player-2", hero.EntityInstanceId)));
  }

  [Fact]
  public void ConcedeCommandExecutionCreatesGameEndedEvent()
  {
    var state = CreateState(PlayerZones.Empty);
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(state, new ConcedeCommand("player-1"));

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("CONCEDED", result.State.TerminalState.Reason);
    Assert.Equal(CommittedEventType.GameEnded, result.Events.Single().EventType);
  }

  [Fact]
  public void CommandExecutionRejectsCommandAfterTerminalState()
  {
    var state = new InMemoryGameState(
      terminalState: new GameTerminalState(
        IsTerminal: true,
        "EMPTY_RESOURCES_DRAW",
        null,
        null,
        "All players have empty decks and hands after turn end."));
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    Assert.Throws<InvalidOperationException>(() =>
      executor.Execute(state, new NoOpCommand("draw")));
  }

  [Fact]
  public void CommandExecutionTurnsAcceptedPipelineFailureIntoEngineFault()
  {
    var mutator = new FailAcceptedBatchThenAllowTerminalMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());
    var state = new InMemoryGameState();

    var result = executor.Execute(state, new NoOpCommand("draw"));

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("ENGINE_FAULT", result.State.TerminalState.Reason);
    Assert.Contains("simulated accepted pipeline failure", result.State.TerminalState.Message, StringComparison.Ordinal);
    var faultEvent = Assert.Single(result.Events);
    Assert.Equal(CommittedEventType.EngineFaulted, faultEvent.EventType);
    Assert.Contains(faultEvent.Payload, entry =>
      entry.Key == "failedCommandKind"
      && entry.Value.StringValue == "draw");
  }

  private sealed class RejectDirectCommandMutator : IStateMutator
  {
    private readonly StateMutator _inner = new();

    public int DirectCommandApplyCount { get; private set; }

    public int BatchApplyCount { get; private set; }

    public ProposedMutationBatch? LastBatch { get; private set; }

    public IGameState Apply(IGameState state, IGameCommand command)
    {
      DirectCommandApplyCount++;
      throw new InvalidOperationException("Direct command mutation path is not allowed in this test.");
    }

    public StateMutationApplication Apply(IGameState state, ProposedMutationBatch batch)
    {
      BatchApplyCount++;
      LastBatch = batch;
      return _inner.Apply(state, batch);
    }
  }

  private sealed class FailAcceptedBatchThenAllowTerminalMutator : IStateMutator
  {
    private readonly StateMutator _inner = new();

    public IGameState Apply(IGameState state, IGameCommand command)
    {
      throw new InvalidOperationException("Direct command mutation path is not allowed in this test.");
    }

    public StateMutationApplication Apply(IGameState state, ProposedMutationBatch batch)
    {
      if (batch.Mutations.Any(mutation =>
        mutation.MutationType == SetTerminalStateMutationHandler.SupportedMutationType))
      {
        return _inner.Apply(state, batch);
      }

      throw new InvalidOperationException("simulated accepted pipeline failure");
    }
  }

  private sealed class ChainDamageEffectCompiler(IStateHasher stateHasher) : IEffectActionCompiler
  {
    private const string TargetTagPrefix = "CHAIN_DAMAGE_TARGET:";

    public EffectActionCompilationResult Compile(EffectActionCompilationRequest request)
    {
      var targetTag = request.Effect.Tags.FirstOrDefault(tag =>
        tag.StartsWith(TargetTagPrefix, StringComparison.Ordinal));
      if (targetTag is null)
      {
        return EffectActionCompilationResult.Accepted(CreateBatch(request, []));
      }

      var parts = targetTag.Split(':', 3);
      var mutation = new StateMutation(
        $"mutation:{request.CommandSeq}:chain-damage:{parts[2]}",
        ApplyDamageMutationHandler.SupportedMutationType,
        request.SourceRef,
        parts[2],
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = 1
        },
        "DEATHRATTLE_PHASE",
        request.CommandSeq,
        null,
        $"chain-damage:{parts[2]}",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["entityInstanceId"] = parts[2],
          ["playerId"] = parts[1]
        });

      return EffectActionCompilationResult.Accepted(CreateBatch(request, [mutation]));
    }

    private ProposedMutationBatch CreateBatch(
      EffectActionCompilationRequest request,
      IReadOnlyList<StateMutation> mutations) =>
      new(
        $"batch:{request.CommandSeq}:chain-damage:{request.Effect.EffectId}",
        request.CommandSeq,
        null,
        "DEATHRATTLE_PHASE",
        request.SourceRef,
        "CHAIN_DAMAGE",
        mutations,
        stateHasher.HashGameplayState(request.State),
        null,
        null);
  }

  private sealed class FixedActionResolver(string actionType) : ICommandActionResolver
  {
    public string ResolveActionType(IGameCommand command)
    {
      ArgumentNullException.ThrowIfNull(command);
      return actionType;
    }
  }

  private static InMemoryGameState CreateState(
    PlayerZones zones,
    string? activePlayerId = "player-1",
    int turnNumber = 0,
    string phase = "TURN_START",
    int mana = 0,
    PlayerZones? secondPlayerZones = null,
    PlayerState? player2 = null,
    IReadOnlyDictionary<string, PendingChoice>? pendingChoices = null,
    EntityInstance? player1Weapon = null,
    EntityInstance? player1Location = null,
    HeroPowerState? heroPower = null,
    IReadOnlyDictionary<string, int>? counters = null,
    IReadOnlyList<PlayerQuest>? player1Quests = null,
    IReadOnlyList<PlayerSecret>? player1Secrets = null,
    IReadOnlyList<PlayerAura>? player1Auras = null,
    IReadOnlyList<PlayerModifier>? player1Modifiers = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Mana = mana,
      MaxMana = mana,
      Zones = zones,
      Weapon = player1Weapon,
      Location = player1Location,
      HeroPower = heroPower,
      Quests = player1Quests ?? [],
      Secrets = player1Secrets ?? [],
      Auras = player1Auras ?? [],
      Modifiers = player1Modifiers ?? []
    };

    return new InMemoryGameState(
      counters: counters,
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player,
        ["player-2"] = player2 ?? PlayerState.Empty("player-2") with
        {
          Zones = secondPlayerZones ?? PlayerZones.Empty
        }
      },
      activePlayerId: activePlayerId,
      turnNumber: turnNumber,
      phase: phase,
      pendingChoices: pendingChoices);
  }

  private static CardInstance CreateCard(
    string cardInstanceId,
    CardType cardType = CardType.MinionCard)
  {
    return new CardInstance(
      cardInstanceId,
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      cardType);
  }

  private static EntityInstance CreateEntity(
    string entityInstanceId,
    string playerId,
    int attack,
    int health,
    EntityType entityType = EntityType.Minion,
    IReadOnlyList<string>? keywords = null) =>
    new(
      entityInstanceId,
      $"card:{entityInstanceId}",
      playerId,
      entityType,
      attack,
      health,
      Damage: 0,
      keywords);

  private static EntityInstance CreateHero(string entityInstanceId, string playerId, int damage) =>
    new(
      entityInstanceId,
      $"hero-card:{playerId}",
      playerId,
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      damage);

  private static CardVersion CreateHeroPowerSourceCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_hero_power_damage",
      EffectKind.AuraEffect,
      [SourceZone.SetAside],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.PlayerChosen,
        new EntityQuery(
          "OPPONENT",
          [SourceZone.Global],
          [EntityType.Hero],
          RequiredTags: [],
          ExcludedTags: [],
          AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE"),
      [
        new DealDamageActionDefinition(
          "action_hero_power_damage",
          new ValueExpression("FIXED", 1, Ref: null),
          new TargetRef("targetRequirement"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["HERO_POWER"]);
    var definition = new CardDefinition(
      "card:hero",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Hero",
        CardType.HeroCard,
        ["mage"],
        Rarity.Legendary,
        7,
        [],
        [],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: null,
        Weapon: null,
        Location: null,
        new HeroCardDefinition(
          5,
          new HeroPowerDefinition(
            2,
            "MAIN_PHASE",
            new TargetSpec(
              Required: true,
              TargetSelectionMode.PlayerChosen,
              new EntityQuery(
                "OPPONENT",
                [SourceZone.Global],
                [EntityType.Hero],
                RequiredTags: [],
                ExcludedTags: [],
                AliveOnly: true),
              new CountSpec(1, 1),
              "FIZZLE"),
            [effect]),
          PortraitAssetRef: null),
        HeroPower: null),
      [],
      new CardTextGeneration("Hero power: Deal 1 damage.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-29T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:hero",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateLocationAbilityCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_location_damage",
      EffectKind.LocationAbilityEffect,
      [SourceZone.SetAside],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.PlayerChosen,
        new EntityQuery(
          "OPPONENT",
          [SourceZone.Global],
          [EntityType.Hero],
          RequiredTags: [],
          ExcludedTags: [],
          AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE"),
      [
        new DealDamageActionDefinition(
          "action_location_damage",
          new ValueExpression("FIXED", 2, Ref: null),
          new TargetRef("targetRequirement"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["LOCATION_ABILITY"]);
    var definition = new CardDefinition(
      "card:location",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Training Grounds",
        CardType.LocationCard,
        ["neutral"],
        Rarity.Common,
        2,
        [],
        [],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: null,
        Weapon: null,
        new LocationDefinition(2, "NONE", "ONCE_PER_TURN"),
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Activate: Deal 2 damage to the enemy hero.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:location",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateDeathrattleDrawCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_deathrattle_draw",
      EffectKind.DeathrattleEffect,
      [SourceZone.Board],
      new TriggerDefinition(EventType.EntityDied, EventFilter: null, "SOURCE_CONTROLLER", OncePerEvent: true),
      new TimingPolicy(Phase.DeathrattlePhase, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new DrawCardActionDefinition(
          "action_deathrattle_draw",
          PlayerSelector.Self,
          new ValueExpression("FIXED", 1, Ref: null),
          "SKIP",
          ResultRef: null)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["DEATHRATTLE"]);
    var definition = new CardDefinition(
      "card:deathrattle-draw",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Last Lesson",
        CardType.MinionCard,
        ["neutral"],
        Rarity.Common,
        2,
        [],
        ["DEATHRATTLE"],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 3, [], ["DEATHRATTLE"], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Deathrattle: Draw a card.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:deathrattle-draw",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateTargetedDeathrattleDamageCardVersion(string cardVersionId)
  {
    return CreateDeathrattleDamageCardVersion(cardVersionId, OpponentHeroAutoTarget());
  }

  private static CardVersion CreatePlayerChosenDeathrattleDamageCardVersion(string cardVersionId)
  {
    return CreateDeathrattleDamageCardVersion(cardVersionId, OpponentHeroPlayerChosenTarget());
  }

  private static CardVersion CreateDeathrattleDamageCardVersion(string cardVersionId, TargetSpec targetRequirement)
  {
    var effect = new EffectDefinition(
      "effect_deathrattle_bolt",
      EffectKind.DeathrattleEffect,
      [SourceZone.Board],
      new TriggerDefinition(EventType.EntityDied, EventFilter: null, "SOURCE_CONTROLLER", OncePerEvent: true),
      new TimingPolicy(Phase.DeathrattlePhase, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: targetRequirement,
      [
        new DealDamageActionDefinition(
          "action_deathrattle_bolt",
          new ValueExpression("FIXED", 2, Ref: null),
          new TargetRef("RESOLVED_TARGETS"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["DEATHRATTLE"]);
    var definition = new CardDefinition(
      "card:deathrattle-bolt",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Last Spark",
        CardType.MinionCard,
        ["neutral"],
        Rarity.Common,
        2,
        [],
        ["DEATHRATTLE"],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 3, [], ["DEATHRATTLE"], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Deathrattle: Deal 2 damage to the enemy hero.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:deathrattle-bolt",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateChainDeathrattleCardVersion(
    string cardVersionId,
    string? nextTargetPlayerId,
    string? nextTargetEntityId)
  {
    var tags = nextTargetPlayerId is null || nextTargetEntityId is null
      ? ["DEATHRATTLE"]
      : new[] { "DEATHRATTLE", $"CHAIN_DAMAGE_TARGET:{nextTargetPlayerId}:{nextTargetEntityId}" };
    var effect = new EffectDefinition(
      $"effect_chain_{cardVersionId}",
      EffectKind.DeathrattleEffect,
      [SourceZone.Board],
      new TriggerDefinition(EventType.EntityDied, EventFilter: null, "SOURCE_CONTROLLER", OncePerEvent: true),
      new TimingPolicy(Phase.DeathrattlePhase, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new DealDamageActionDefinition(
          $"action_chain_{cardVersionId}",
          new ValueExpression("FIXED", 1, Ref: null),
          new TargetRef("CHAIN_DAMAGE_TARGET"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      tags);
    var definition = new CardDefinition(
      $"card:chain:{cardVersionId}",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        $"Chain {cardVersionId}",
        CardType.MinionCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["DEATHRATTLE"],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(0, 1, [], ["DEATHRATTLE"], CanAttack: false),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Deathrattle: continue the chain.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-02T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      $"card:chain:{cardVersionId}",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-07-02T00:00:00Z"),
      definition);
  }

  private static InMemoryGameState CreateDeathrattleTargetChoiceState(
    EntityInstance attacker,
    CardInstance defenderCard,
    EntityInstance defender,
    IReadOnlyList<EntityInstance>? additionalPlayer1Board = null) =>
    new(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = CreateHero("hero-player-1", "player-1", damage: 0),
          Zones = new PlayerZones(board: [attacker, .. (additionalPlayer1Board ?? [])])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = CreateHero("hero-player-2", "player-2", damage: 0),
          Zones = new PlayerZones(setAside: [defenderCard], board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");

  private static CardVersion CreateSecretDrawCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_secret_draw",
      EffectKind.SecretEffect,
      [SourceZone.Hand, SourceZone.SecretZone],
      new TriggerDefinition(EventType.CardSummoned, EventFilter: null, "OPPONENT", OncePerEvent: true),
      new TimingPolicy(Phase.SecretWindow, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new DrawCardActionDefinition(
          "action_secret_draw",
          PlayerSelector.Self,
          new ValueExpression("FIXED", 1, Ref: null),
          "SKIP",
          ResultRef: null)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["SECRET_REVEAL"]);
    var definition = new CardDefinition(
      "card:secret-draw",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Hidden Lesson", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["SECRET"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SECRET", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Secret: When your opponent summons a minion, draw a card.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:secret-draw", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), definition);
  }

  private static CardVersion CreateBoardAttackBuffSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_board_attack_buff",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new ApplyModifierActionDefinition(
          "action_board_attack_buff",
          PlayerSelector.Self,
          new ModifierDefinition(
            "modifier_board_attack_buff",
            PlayerModifierTypes.BoardAttackModifier,
            new ValueExpression("FIXED", 2, Ref: null),
            PlayerModifierDurations.Permanent,
            Priority: 10))
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["BOARD_ATTACK_MODIFIER"]);
    var definition = new CardDefinition(
      "card:board-attack-buff",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Battle Chant", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["BOARD_ATTACK_MODIFIER"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Give your minions +2 Attack.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:board-attack-buff", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateDamageDistributionSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_damage_distribution",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new DamageDistributionActionDefinition(
          "action_damage_distribution",
          new ValueExpression("FIXED", 3, Ref: null),
          new EntityQuery("OPPONENT", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
          "SOURCE_ORDER",
          "SEPARATE_DAMAGE_INSTANCES",
          "NO_LIFESTEAL",
          "ALLOW_OVERKILL",
          FizzleIfNoTargets: false,
          "NONE")
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["DAMAGE_DISTRIBUTION"]);
    var definition = new CardDefinition(
      "card:damage-distribution",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Splash Volley", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["DAMAGE_DISTRIBUTION"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Deal 3 damage split across enemy minions in source order.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:damage-distribution", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateExtraTurnSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_extra_turn",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new TakeExtraTurnActionDefinition(
          "action_extra_turn",
          PlayerSelector.Self,
          OncePerGame: true,
          CurrentTurnEndsNormally: true,
          "AFTER_CURRENT_TURN",
          "QUEUE_AFTER_CURRENT_TURN",
          CommandButtonReplacement: null,
          "EXPIRE_CURRENT_TURN_ONLY")
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["TAKE_EXTRA_TURN_ACTION"]);
    var definition = new CardDefinition(
      "card:extra-turn",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Borrowed Moment", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["TAKE_EXTRA_TURN_ACTION"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Take an extra turn after this one.", FlavorText: null),
      new BalanceMetadata(5, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:extra-turn", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateApplyAuraMinionCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_apply_aura",
      EffectKind.AuraEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new ApplyAuraActionDefinition(
          "action_apply_aura",
          PlayerSelector.Self,
          new AuraDefinition(
            "aura:entity-aura-source:effect_apply_aura",
            "BOARD_STAT_AURA",
            new EntityQuery("SELF", [SourceZone.Board], [EntityType.Minion], [], [], AliveOnly: true),
            new ModifierDefinition(
              "modifier_apply_aura_attack",
              PlayerModifierTypes.BoardAttackModifier,
              new ValueExpression("FIXED", 1, Ref: null),
              PlayerModifierDurations.WhileSourceActive,
              Priority: 0),
            PlayerModifierDurations.WhileSourceActive,
            Priority: 0))
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["APPLY_AURA"]);
    var definition = new CardDefinition(
      "card:apply-aura",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Banner Initiate", CardType.MinionCard, ["neutral"], Rarity.Common, 2, [], ["APPLY_AURA"], true, ["mvp"]),
      new EntityDefinition(new MinionDefinition(1, 3, [], ["APPLY_AURA"], CanAttack: true), Spell: null, Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Aura: Your board is tracked as an active aura source.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:apply-aura", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateReplaceEventSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_replace_damage",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.AutoValid,
        new EntityQuery("SELF", [SourceZone.Global], [EntityType.Hero], [], [], AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE"),
      [
        new ReplaceEventActionDefinition(
          "action_replace_damage",
          "DAMAGE_WOULD_BE_DEALT",
          new TargetRef("TARGET"),
          "PREVENT_DAMAGE",
          PlayerModifierDurations.NextDamage,
          "replacement_prevent_next_damage")
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["REPLACE_EVENT"]);
    var definition = new CardDefinition(
      "card:replace-event",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Guarded Moment", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["REPLACE_EVENT"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Prevent the next damage that would be dealt to your hero.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:replace-event", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateBoardKeywordBuffSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_board_keyword_buff",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new ApplyModifierActionDefinition(
          "action_board_keyword_buff",
          PlayerSelector.Self,
          new ModifierDefinition(
            "modifier_board_keyword_buff",
            PlayerModifierTypes.BoardKeywordModifier,
            new ValueExpression("KEYWORD", FixedValue: null, Ref: "TAUNT"),
            PlayerModifierDurations.Permanent,
            Priority: 10))
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["BOARD_KEYWORD_MODIFIER"]);
    var definition = new CardDefinition(
      "card:board-keyword-buff",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Shield Chant", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["BOARD_KEYWORD_MODIFIER"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Give your minions Taunt.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:board-keyword-buff", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateHandZoneModifierSpellCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_hand_zone_modifier",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new ApplyModifierActionDefinition(
          "action_hand_zone_modifier",
          PlayerSelector.Self,
          new ModifierDefinition(
            "modifier_hand_zone",
            PlayerModifierTypes.HandZoneModifier,
            new ValueExpression("ZONE", FixedValue: null, Ref: "GRAVEYARD"),
            PlayerModifierDurations.Permanent,
            Priority: 10))
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["HAND_ZONE_MODIFIER"]);
    var definition = new CardDefinition(
      "card:hand-zone-modifier",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Grave Recall", CardType.SpellCard, ["neutral"], Rarity.Common, 1, [], ["HAND_ZONE_MODIFIER"], true, ["mvp"]),
      new EntityDefinition(Minion: null, new SpellDefinition("SPELL", RequiresTarget: false), Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Move your hand to your graveyard.", FlavorText: null),
      new BalanceMetadata(4, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:hand-zone-modifier", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), definition);
  }

  private static CardVersion CreateAuraCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_aura_refresh",
      EffectKind.AuraEffect,
      [SourceZone.Board],
      Trigger: null,
      new TimingPolicy(Phase.AuraRefresh, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      Actions: [],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["AURA_REFRESH"]);
    var definition = new CardDefinition(
      "card:aura-source",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData("Banner Keeper", CardType.MinionCard, ["neutral"], Rarity.Common, 2, [], ["AURA"], true, ["mvp"]),
      new EntityDefinition(new MinionDefinition(1, 3, [], ["AURA"], CanAttack: true), Spell: null, Weapon: null, Location: null, HeroCard: null, HeroPower: null),
      [effect],
      new CardTextGeneration("Aura source.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion("card:aura-source", cardVersionId, "dsl-v1", "engine-v1", "catalog-alpha", "pipeline-v1", "ruleset_mvp_core_v1", "sha256:ruleset", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), definition);
  }

  private static CardVersion CreateQuestRewardDrawCardVersion(string cardVersionId)
  {
    var effect = new EffectDefinition(
      "effect_quest_reward_draw",
      EffectKind.QuestEffect,
      [SourceZone.QuestZone],
      Trigger: null,
      new TimingPolicy(Phase.AfterPlay, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new DrawCardActionDefinition(
          "action_quest_reward_draw",
          PlayerSelector.Self,
          new ValueExpression("FIXED", 1, Ref: null),
          "SKIP",
          ResultRef: null)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["BASIC_QUEST"]);
    var definition = new CardDefinition(
      "card:quest-draw",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "First Lesson",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["BASIC_QUEST"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("QUEST", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Quest: Summon a minion. Reward: Draw a card.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:quest-draw",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateTargetedQuestRewardDamageCardVersion(string cardVersionId)
  {
    return CreateQuestRewardDamageCardVersion(cardVersionId, OpponentHeroAutoTarget());
  }

  private static CardVersion CreatePlayerChosenQuestRewardDamageCardVersion(string cardVersionId)
  {
    return CreateQuestRewardDamageCardVersion(cardVersionId, OpponentHeroPlayerChosenTarget());
  }

  private static CardVersion CreateQuestRewardDamageCardVersion(string cardVersionId, TargetSpec targetRequirement)
  {
    var effect = new EffectDefinition(
      "effect_quest_reward_bolt",
      EffectKind.QuestEffect,
      [SourceZone.QuestZone],
      Trigger: null,
      new TimingPolicy(Phase.AfterPlay, Window: null, Priority: 0, OrderingPolicy: "SOURCE_ORDER"),
      Conditions: [],
      TargetRequirement: targetRequirement,
      [
        new DealDamageActionDefinition(
          "action_quest_reward_bolt",
          new ValueExpression("FIXED", 3, Ref: null),
          new TargetRef("RESOLVED_TARGETS"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("SKIP"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["BASIC_QUEST"]);
    var definition = new CardDefinition(
      "card:quest-bolt",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Final Lesson",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["BASIC_QUEST"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("QUEST", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Quest: Summon a minion. Reward: Deal 3 damage to the enemy hero.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(null, null, null),
      new AuthoringMetadata("test", DateTimeOffset.Parse("2026-06-30T00:00:00Z"), "manual-fixture", []));

    return new CardVersion(
      "card:quest-bolt",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
      definition);
  }

  private static PlayerQuest CreateQuestRewardBoltQuest(CardInstance questSource) =>
    new(
      "quest-play-minion",
      questSource.CardInstanceId,
      "player-1",
      "MINION_SUMMONED",
      Progress: 0,
      RequiredProgress: 1,
      IsCompleted: false,
      RewardEffectId: "effect_quest_reward_bolt",
      SourceCardVersion: questSource.CardVersion);

  private static TargetSpec OpponentHeroAutoTarget() =>
    new(
      Required: true,
      TargetSelectionMode.AutoValid,
      new EntityQuery(
        "OPPONENT",
        [SourceZone.Global],
        [EntityType.Hero],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(1, 1),
      RetargetPolicy: "FIZZLE");

  private static TargetSpec OpponentHeroPlayerChosenTarget() =>
    new(
      Required: true,
      TargetSelectionMode.PlayerChosen,
      new EntityQuery(
        "OPPONENT",
        [SourceZone.Global],
        [EntityType.Hero],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(1, 1),
      RetargetPolicy: "FIZZLE");

  private static TargetSpec OpponentBoardMinionsPlayerChosenTarget(int count) =>
    new(
      Required: true,
      TargetSelectionMode.PlayerChosen,
      new EntityQuery(
        "OPPONENT",
        [SourceZone.Board],
        [EntityType.Minion],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(count, count),
      RetargetPolicy: "FIZZLE");
}
