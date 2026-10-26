using Game.Application.Replay;
using Game.Application.Execution;
using Game.Application.Capabilities;
using Game.Application.Reconstruction;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class ReplayVerificationServiceTests
{
  private readonly IStateHasher hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void VerifyAcceptsMatchingFinalStateHash()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(2, "player-1", "play")
    ]);
    var expectedHash = hasher.HashGameplayState(CreateReplayer().Replay(initialState, commandLog).State);
    var service = CreateService();

    var result = service.Verify(initialState, commandLog, expectedHash);

    Assert.True(result.IsVerified);
    Assert.Equal(expectedHash, result.FinalStateHash);
    Assert.Equal(2, result.DerivedEventCount);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void VerifyReturnsDeterministicDebugTraceForRegeneratedEvents()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(2, "player-1", "play")
    ]);
    var expectedHash = hasher.HashGameplayState(CreateReplayer().Replay(initialState, commandLog).State);
    var service = CreateService();

    var first = service.Verify(initialState, commandLog, expectedHash);
    var second = service.Verify(initialState, commandLog, expectedHash);

    Assert.True(first.IsVerified);
    Assert.NotNull(first.DebugTrace);
    Assert.Equal("replay-debug-trace-v1", first.DebugTrace.TraceVersion);
    Assert.StartsWith("sha256:", first.DebugTrace.TraceHash, StringComparison.Ordinal);
    Assert.Equal(first.DerivedEventCount, first.DebugTrace.Entries.Count);
    Assert.Equal(first.DebugTrace.TraceHash, second.DebugTrace?.TraceHash);
    Assert.All(first.DebugTrace.Entries, entry => Assert.NotNull(entry.EventType));
  }

  [Fact]
  public void VerifyAcceptsMatchingInitialStateHash()
  {
    var initialState = new InMemoryGameState().WithCounter("life", 20);
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw")
    ]);
    var expectedInitialHash = hasher.HashGameplayState(initialState);
    var expectedFinalHash = hasher.HashGameplayState(CreateReplayer().Replay(initialState, commandLog).State);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      expectedInitialHash));

    Assert.True(result.IsVerified);
    Assert.Equal(expectedFinalHash, result.FinalStateHash);
  }

  [Fact]
  public void VerifyRejectsInitialStateHashMismatchBeforeReplayExecution()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw")
    ]);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      "sha256:any-final",
      "sha256:not-initial"));

    Assert.False(result.IsVerified);
    Assert.Null(result.FinalStateHash);
    Assert.Equal(0, result.DerivedEventCount);
    Assert.Null(result.DebugTrace);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "INITIAL_STATE_HASH_MISMATCH"
      && diagnostic.Path == "replay.initialStateHash");
  }

  [Fact]
  public void VerifyRejectsFinalStateHashMismatch()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw")
    ]);
    var service = CreateService();

    var result = service.Verify(initialState, commandLog, "sha256:not-matching");

    Assert.False(result.IsVerified);
    Assert.NotNull(result.FinalStateHash);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FINAL_STATE_HASH_MISMATCH");
  }

  [Fact]
  public void VerifyRejectsMissingExpectedFinalStateHash()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw")
    ]);
    var service = CreateService();

    var result = service.Verify(initialState, commandLog, null);

    Assert.False(result.IsVerified);
    Assert.NotNull(result.FinalStateHash);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EXPECTED_FINAL_STATE_HASH_REQUIRED");
  }

  [Fact]
  public void VerifyRejectsCapabilityPreflightBeforeReplayExecution()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw")
    ]);
    var registry = new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry();
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      "sha256:any-final",
      hasher.HashGameplayState(initialState),
      EngineCapabilityRegistry: registry,
      RulesetProfile: TestRulesetProfileFactory.CreateProfile("resolved-ruleset-mvp-core-v1"),
      CatalogSnapshot: EngineCapabilityAuditServiceTests.CreateCatalog(EngineCapabilityAuditServiceTests.CreateSupportedCard()),
      ExpectedEngineCapabilityRegistryHash: "sha256:not-current"));

    Assert.False(result.IsVerified);
    Assert.Null(result.FinalStateHash);
    Assert.Equal(0, result.DerivedEventCount);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "ENGINE_CAPABILITY_REGISTRY_HASH_MISMATCH"
      && diagnostic.Path == "replay.preflight.engineCapabilityRegistry.hash");
  }

  [Fact]
  public void VerifyRejectsReplayExecutionFailureWithoutMutatingInitialState()
  {
    var initialState = new InMemoryGameState();
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        "draw",
        expectedPreStateHash: "not-current-state")
    ]);
    var service = CreateService();

    var result = service.Verify(initialState, commandLog, "sha256:any");

    Assert.False(result.IsVerified);
    Assert.Null(result.FinalStateHash);
    Assert.Equal(0, result.DerivedEventCount);
    Assert.Equal(0, initialState.Revision);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPLAY_EXECUTION_FAILED");
  }

  [Fact]
  public void VerifyRejectsPlayCardPayloadMissingRefsBeforeMutation()
  {
    var card = new CardInstance(
      "card-instance-1",
      "card:river-guard",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(hand: [card])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      });
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 2,
          ["health"] = 3
        })
    ]);
    var service = CreateService();

    var result = service.Verify(initialState, commandLog, "sha256:any");

    Assert.False(result.IsVerified);
    Assert.Null(result.FinalStateHash);
    Assert.Equal(0, result.DerivedEventCount);
    Assert.Single(initialState.Players["player-1"].Zones.Hand);
    Assert.Empty(initialState.Players["player-1"].Zones.Board);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "REPLAY_EXECUTION_FAILED");
  }

  [Fact]
  public void VerifyComparesRegeneratedRngRollRefs()
  {
    var card = new CardInstance(
      "card-instance-random",
      "card:random-bolt",
      "card_random_bolt_v1",
      "player-1",
      CardType.MinionCard);
    var opponent = new EntityInstance(
      "entity-opponent-a",
      "card:opponent-a",
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 3,
          MaxMana = 3,
          Zones = new PlayerZones(hand: [card])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(board: [opponent])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[] { CreateRandomTargetCardVersion() };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-random-bolt"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replayer = CreateReplayer(catalogCards);
    var replay = replayer.Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var expectedRngRefs = replay.DerivedEvents.SelectMany(@event => @event.RngRollRefs).ToArray();
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var accepted = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      expectedRngRefs));
    var expectedTrace = accepted.RngTrace;
    var rejected = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      ["rng:tampered"]));
    var traceAccepted = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      expectedRngRefs,
      expectedTrace));
    var tamperedTrace = expectedTrace! with
    {
      Rolls =
      [
        expectedTrace.Rolls.Single() with
        {
          SelectedIndex = expectedTrace.Rolls.Single().SelectedIndex + 1
        }
      ]
    };
    var traceRejected = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      expectedRngRefs,
      tamperedTrace));

    Assert.True(accepted.IsVerified);
    Assert.NotEmpty(accepted.RngRollRefs);
    Assert.NotNull(expectedTrace);
    var roll = Assert.Single(expectedTrace.Rolls);
    Assert.Equal("RANDOM_TARGET", roll.Stream);
    Assert.Equal(1, roll.CandidateCount);
    Assert.Equal(1, roll.CommandSeq);
    Assert.Single(roll.SelectedTargetRefs);
    Assert.StartsWith("sha256:", expectedTrace.TraceHash, StringComparison.Ordinal);
    Assert.True(traceAccepted.IsVerified, string.Join("; ", traceAccepted.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Message}:{diagnostic.Identifier}")));
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.RngRolled
      && @event.Category == EventCategory.Resolver
      && @event.Visibility == EventVisibility.DebugOnly);
    Assert.False(rejected.IsVerified);
    Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "RNG_ROLL_REFS_MISMATCH");
    Assert.False(traceRejected.IsVerified);
    Assert.Contains(traceRejected.Diagnostics, diagnostic => diagnostic.Code == "RNG_TRACE_MISMATCH");
  }

  [Fact]
  public void VerifyComparesRandomSelectBatchDestroyRngRollRefs()
  {
    var card = new CardInstance(
      "card-instance-brawl",
      "card:brawl",
      "card_brawl_v1",
      "player-1",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(
            hand: [card],
            board:
            [
              CreateReplayMinion("entity-self-a", "player-1"),
              CreateReplayMinion("entity-self-b", "player-1")
            ])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(board:
          [
            CreateReplayMinion("entity-opponent-a", "player-2"),
            CreateReplayMinion("entity-opponent-b", "player-2")
          ])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[] { CreateRandomSelectBrawlCardVersion() };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-brawl"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var expectedRngRefs = replay.DerivedEvents.SelectMany(@event => @event.RngRollRefs).ToArray();
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var accepted = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      expectedRngRefs));

    Assert.True(accepted.IsVerified);
    Assert.Single(expectedRngRefs);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.RngRolled
      && @event.TargetRefs.Count == 1);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.EntityDestroyed);
  }

  [Fact]
  public void VerifyReplaysHistoryQueryResurrectSelection()
  {
    var card = new CardInstance(
      "card-instance-resurrector",
      "card:resurrector",
      "card_resurrector_v1",
      "player-1",
      CardType.MinionCard);
    var firstDead = new CardInstance(
      "card-dead-minion-a",
      "card:dead-minion-a",
      "dead-minion-v1",
      "player-1",
      CardType.MinionCard);
    var lastDead = new CardInstance(
      "card-dead-minion-b",
      "card:dead-minion-b",
      "dead-minion-v1",
      "player-1",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(
            hand: [card],
            graveyard: [firstDead, lastDead])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[] { CreateHistoryResurrectCardVersion() };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1,
          ["maxBoardSize"] = 7
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-resurrector"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.State.Players["player-1"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, firstDead.CardInstanceId));
    Assert.DoesNotContain(replay.State.Players["player-1"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, lastDead.CardInstanceId));
    Assert.Contains(replay.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, lastDead.CardInstanceId));
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.MinionSummoned);
  }

  [Fact]
  public void VerifyReplaysDamageDistributionSpellEffect()
  {
    var card = new CardInstance(
      "card-splash-volley",
      "card:damage-distribution",
      "card_damage_distribution_v1",
      "player-1",
      CardType.SpellCard);
    var first = new EntityInstance(
      "entity-opponent-a",
      "card:minion-a",
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 5,
      Damage: 0);
    var second = new EntityInstance(
      "entity-opponent-b",
      "card:minion-b",
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 5,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [card])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(board: [second, first])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
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
          ["cardInstanceId"] = card.CardInstanceId
        },
        hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateDamageDistributionSpellCardVersion(card.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(2, replay.State.Players["player-2"].Zones.Board
      .Single(entity => entity.EntityInstanceId == first.EntityInstanceId)
      .Damage);
    Assert.Equal(1, replay.State.Players["player-2"].Zones.Board
      .Single(entity => entity.EntityInstanceId == second.EntityInstanceId)
      .Damage);
    Assert.Equal(3, replay.DerivedEvents.Count(@event =>
      @event.EventType == CommittedEventType.EntityDamaged));
  }

  [Fact]
  public void ReplayMatchesLiveCommandScopedRngAcrossMultipleRandomSpells()
  {
    var firstCard = new CardInstance("card-splash-volley-a", "card:damage-distribution", "card_damage_distribution_v1", "player-1", CardType.SpellCard);
    var secondCard = new CardInstance("card-splash-volley-b", "card:damage-distribution", "card_damage_distribution_v1", "player-1", CardType.SpellCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with { Mana = 2, MaxMana = 2, Zones = new PlayerZones(hand: [firstCard, secondCard]) },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(board:
          [
            new EntityInstance("entity-opponent-a", "card:minion-a", "player-2", EntityType.Minion, 1, 10, 0),
            new EntityInstance("entity-opponent-b", "card:minion-b", "player-2", EntityType.Minion, 1, 10, 0)
          ])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[] { CreateDamageDistributionSpellCardVersion(firstCard.CardVersion) };
    var liveState = (IGameState)initialState;
    var payloads = new List<DeterministicCommandPayload>();
    foreach (var card in new[] { firstCard, secondCard })
    {
      var preHash = hasher.HashGameplayState(liveState);
      var payload = new DeterministicCommandPayload(
        payloads.Count + 1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal) { ["manaCost"] = 1 },
        new SortedDictionary<string, string>(StringComparer.Ordinal) { ["cardInstanceId"] = card.CardInstanceId },
        preHash);
      payloads.Add(payload);
      liveState = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog(), catalogCards).Execute(liveState, payload.ToCommand()).State;
    }

    var replay = CreateReplayer(catalogCards).Replay(initialState, new CanonicalCommandLog(payloads));

    Assert.Equal(hasher.HashGameplayState(liveState), hasher.HashGameplayState(replay.State));
  }

  [Fact]
  public void VerifyReplaysBoardAttackModifierSpellEffect()
  {
    var card = new CardInstance(
      "card-board-buff",
      "card:board-attack-buff",
      "card_board_attack_buff_v1",
      "player-1",
      CardType.SpellCard);
    var first = new EntityInstance(
      "entity-board-a",
      "card:minion-a",
      "player-1",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var second = new EntityInstance(
      "entity-board-b",
      "card:minion-b",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 4,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [card], board: [first, second])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
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
          ["cardInstanceId"] = card.CardInstanceId
        },
        hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateBoardAttackBuffSpellCardVersion(card.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(
      [3, 4],
      replay.State.Players["player-1"].Zones.Board
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => entity.Attack));
    Assert.Equal(2, replay.DerivedEvents.Count(@event =>
      @event.EventType == CommittedEventType.ModifierApplied
      && @event.TargetRefs.Any(target => target.RefType == "ENTITY_INSTANCE")));
  }

  [Fact]
  public void VerifyReplaysBoardKeywordModifierSpellEffect()
  {
    var card = new CardInstance(
      "card-board-keyword",
      "card:board-keyword-buff",
      "card_board_keyword_buff_v1",
      "player-1",
      CardType.SpellCard);
    var minion = new EntityInstance(
      "entity-board-a",
      "card:minion-a",
      "player-1",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [card], board: [minion])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
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
          ["cardInstanceId"] = card.CardInstanceId
        },
        hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateBoardKeywordBuffSpellCardVersion(card.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(["TAUNT"], Assert.Single(replay.State.Players["player-1"].Zones.Board).Keywords);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.ModifierApplied
      && @event.Payload.Any(payload => payload.Key == "keyword" && payload.Value.StringValue == "TAUNT"));
  }

  [Fact]
  public void VerifyReplaysHandZoneModifierSpellEffect()
  {
    var card = new CardInstance(
      "card-hand-zone-modifier",
      "card:hand-zone-modifier",
      "card_hand_zone_modifier_v1",
      "player-1",
      CardType.SpellCard);
    var handA = new CardInstance(
      "card-hand-b",
      "card:hand-b",
      "card_hand_b_v1",
      "player-1",
      CardType.SpellCard);
    var handB = new CardInstance(
      "card-hand-a",
      "card:hand-a",
      "card_hand_a_v1",
      "player-1",
      CardType.SpellCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Zones = new PlayerZones(hand: [card, handA, handB])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
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
          ["cardInstanceId"] = card.CardInstanceId
        },
        hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateHandZoneModifierSpellCardVersion(card.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Empty(replay.State.Players["player-1"].Zones.Hand);
    Assert.Equal(
      [card.CardInstanceId, "card-hand-a", "card-hand-b"],
      replay.State.Players["player-1"].Zones.Graveyard.Select(graveyardCard => graveyardCard.CardInstanceId));
    Assert.Equal(3, replay.DerivedEvents.Count(@event => @event.EventType == CommittedEventType.CardMovedZone));
  }

  [Fact]
  public void VerifyReplaysOpponentHistoryQueryResurrectSelection()
  {
    var card = new CardInstance(
      "card-instance-resurrector",
      "card:resurrector",
      "card_resurrector_v1",
      "player-1",
      CardType.MinionCard);
    var firstDead = new CardInstance(
      "card-opponent-dead-minion-a",
      "card:opponent-dead-minion-a",
      "dead-minion-v1",
      "player-2",
      CardType.MinionCard);
    var lastDead = new CardInstance(
      "card-opponent-dead-minion-b",
      "card:opponent-dead-minion-b",
      "dead-minion-v1",
      "player-2",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(hand: [card])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(graveyard: [firstDead, lastDead])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[]
    {
      CreateHistoryResurrectCardVersion(
        historyQueryKind: "OPPONENT_GRAVEYARD_MINION",
        historyQueryPlayer: PlayerSelector.Opponent)
    };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1,
          ["maxBoardSize"] = 7
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-resurrector"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.State.Players["player-2"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, firstDead.CardInstanceId));
    Assert.DoesNotContain(replay.State.Players["player-2"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, lastDead.CardInstanceId));
    Assert.Contains(replay.State.Players["player-2"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, lastDead.CardInstanceId));
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.MinionSummoned);
  }

  [Fact]
  public void VerifyReplaysAnyGraveyardHistoryQueryResurrectSelection()
  {
    var card = new CardInstance(
      "card-instance-resurrector",
      "card:resurrector",
      "card_resurrector_v1",
      "player-1",
      CardType.MinionCard);
    var ownDead = new CardInstance(
      "card-own-dead-minion",
      "card:own-dead-minion",
      "dead-minion-v1",
      "player-1",
      CardType.MinionCard);
    var opponentDead = new CardInstance(
      "card-opponent-dead-minion",
      "card:opponent-dead-minion",
      "dead-minion-v1",
      "player-2",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(
            hand: [card],
            graveyard: [ownDead])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(graveyard: [opponentDead])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[]
    {
      CreateHistoryResurrectCardVersion(
        historyQueryKind: "ANY_GRAVEYARD_MINION",
        historyQueryPlayer: PlayerSelector.Self)
    };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1,
          ["maxBoardSize"] = 7
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-resurrector"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.State.Players["player-1"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, ownDead.CardInstanceId));
    Assert.DoesNotContain(replay.State.Players["player-2"].Zones.Graveyard, cardInGraveyard =>
      StringComparer.Ordinal.Equals(cardInGraveyard.CardInstanceId, opponentDead.CardInstanceId));
    Assert.Contains(replay.State.Players["player-2"].Zones.SetAside, moved =>
      StringComparer.Ordinal.Equals(moved.CardInstanceId, opponentDead.CardInstanceId));
    Assert.Contains(replay.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, opponentDead.CardInstanceId)
      && StringComparer.Ordinal.Equals(entity.ControllerPlayerId, "player-1"));
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.MinionSummoned);
  }

  [Fact]
  public void VerifyReplaysDeadMinionHistoryQueryFromPriorCommittedDeathEvent()
  {
    var resurrector = new CardInstance(
      "card-instance-resurrector",
      "card:resurrector",
      "card_resurrector_v1",
      "player-1",
      CardType.MinionCard);
    var attacker = new EntityInstance(
      "entity-attacker",
      "card-attacker",
      "player-1",
      EntityType.Minion,
      Attack: 3,
      Health: 5,
      Damage: 0);
    var deadSource = new CardInstance(
      "card-opponent-dead-minion",
      "card:opponent-dead-minion",
      "dead-minion-v1",
      "player-2",
      CardType.MinionCard);
    var defender = new EntityInstance(
      "entity-opponent-dead-minion",
      deadSource.CardInstanceId,
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(hand: [resurrector], board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(setAside: [deadSource], board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defender.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState)),
      new DeterministicCommandPayload(
        2,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1,
          ["maxBoardSize"] = 7
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = resurrector.CardInstanceId,
          ["entityInstanceId"] = "entity-resurrector"
        })
    ]);
    var catalogCards = new[]
    {
      CreateHistoryResurrectCardVersion(
        historyQueryKind: "ANY_DEAD_MINION",
        historyQueryPlayer: PlayerSelector.Self)
    };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.EntityDied);
    Assert.Contains(replay.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, deadSource.CardInstanceId)
      && StringComparer.Ordinal.Equals(entity.ControllerPlayerId, "player-1"));
    Assert.DoesNotContain(replay.State.Players["player-2"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, deadSource.CardInstanceId));
  }

  [Fact]
  public void VerifyReplaysRandomHistoryQueryResurrectSelectionWithRngTrace()
  {
    var card = new CardInstance(
      "card-instance-resurrector",
      "card:resurrector",
      "card_resurrector_v1",
      "player-1",
      CardType.MinionCard);
    var firstDead = new CardInstance(
      "card-dead-minion-a",
      "card:dead-minion-a",
      "dead-minion-v1",
      "player-1",
      CardType.MinionCard);
    var secondDead = new CardInstance(
      "card-dead-minion-b",
      "card:dead-minion-b",
      "dead-minion-v1",
      "player-1",
      CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 5,
          MaxMana = 5,
          Zones = new PlayerZones(
            hand: [card],
            graveyard: [firstDead, secondDead])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var catalogCards = new[] { CreateHistoryResurrectCardVersion("RANDOM") };
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1,
          ["maxBoardSize"] = 7
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-resurrector"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var expectedRngRefs = replay.DerivedEvents.SelectMany(@event => @event.RngRollRefs).ToArray();
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState),
      expectedRngRefs));

    Assert.True(result.IsVerified);
    var roll = Assert.Single(result.RngTrace?.Rolls ?? []);
    Assert.Equal("HISTORY_QUERY", roll.Reason);
    Assert.Equal("RANDOM_TARGET", roll.Stream);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.RngRolled);
    Assert.Single(replay.State.Players["player-1"].Zones.Board, entity =>
      StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, firstDead.CardInstanceId)
      || StringComparer.Ordinal.Equals(entity.SourceCardInstanceId, secondDead.CardInstanceId));
  }

  [Fact]
  public void VerifyReplaysUseHeroPowerCommand()
  {
    var sourceCard = new CardInstance(
      "hero-card-instance-1",
      "card:hero",
      "card_hero_v1",
      "player-1",
      CardType.HeroCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 2,
          MaxMana = 2,
          HeroPower = new HeroPowerState(sourceCard.CardInstanceId, 2, "MAIN_PHASE"),
          Zones = new PlayerZones(setAside: [sourceCard])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        UseHeroPowerCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["manaCost"] = 2
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["sourceCardInstanceId"] = sourceCard.CardInstanceId
        },
        hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer().Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(0, replay.State.Players["player-1"].Mana);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.HeroPowerUsed);
  }

  [Fact]
  public void VerifyReplaysWeaponAttackDurabilityAndDestruction()
  {
    var weaponCard = new CardInstance(
      "weapon-instance-1",
      "card:weapon",
      "card_weapon_v1",
      "player-1",
      CardType.WeaponCard);
    var weapon = new EntityInstance(
      "weapon:weapon-instance-1",
      weaponCard.CardInstanceId,
      "player-1",
      EntityType.Weapon,
      Attack: 2,
      Health: 1,
      Damage: 0);
    var defendingHero = new EntityInstance(
      "hero-player-2",
      "hero-card-player-2",
      "player-2",
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = new EntityInstance("hero-player-1", "hero-card-player-1", "player-1", EntityType.Hero, 0, 30, 0),
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
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = weapon.EntityInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defendingHero.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer().Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(2, replay.State.Players["player-2"].Hero?.Damage);
    Assert.Null(replay.State.Players["player-1"].Weapon);
    Assert.Contains(replay.State.Players["player-1"].Zones.Graveyard, card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, weaponCard.CardInstanceId));
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.EntityDestroyed);
  }

  [Fact]
  public void VerifyReplaysDivineShieldConsumptionForAttackDamage()
  {
    var attacker = new EntityInstance(
      "entity-attacker",
      "card:attacker",
      "player-1",
      EntityType.Minion,
      Attack: 3,
      Health: 3,
      Damage: 0);
    var defender = new EntityInstance(
      "entity-defender",
      "card:defender",
      "player-2",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0,
      Keywords: ["DIVINE_SHIELD"]);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Hero = new EntityInstance("hero-player-1", "hero-card-player-1", "player-1", EntityType.Hero, 0, 30, 0),
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = new EntityInstance("hero-player-2", "hero-card-player-2", "player-2", EntityType.Hero, 0, 30, 0),
          Zones = new PlayerZones(board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defender.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer().Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    var replayedDefender = Assert.Single(replay.State.Players["player-2"].Zones.Board);
    Assert.Equal(0, replayedDefender.Damage);
    Assert.Null(replayedDefender.Keywords);
    Assert.Equal(1, replay.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.Category == EventCategory.Proposed
      && @event.EventType == CommittedEventType.DamageWouldBeDealt);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.Category == EventCategory.Resolver
      && @event.EventType == CommittedEventType.ReplacementResolved);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.ModifierExpired
      && @event.Payload.Any(payload => payload.Key == "keyword" && payload.Value.StringValue == "DIVINE_SHIELD"));
  }

  [Fact]
  public void VerifyReplaysActivateLocationCommandWithEffect()
  {
    var locationCard = new CardInstance(
      "location-instance-1",
      "card:location",
      "card_location_v1",
      "player-1",
      CardType.LocationCard);
    var location = new EntityInstance(
      "location:location-instance-1",
      locationCard.CardInstanceId,
      "player-1",
      EntityType.Location,
      Attack: 0,
      Health: 2,
      Damage: 0);
    var defendingHero = new EntityInstance(
      "hero-player-2",
      "hero-card-player-2",
      "player-2",
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      Damage: 0);
    var initialState = new InMemoryGameState(
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
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        ActivateLocationCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["locationEntityInstanceId"] = location.EntityInstanceId,
          ["sourceCardInstanceId"] = locationCard.CardInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defendingHero.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateLocationAbilityCardVersion(locationCard.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(1, replay.State.Players["player-1"].Location?.Damage);
    Assert.Equal(2, replay.State.Players["player-2"].Hero?.Damage);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.LocationActivated);
  }

  [Fact]
  public void VerifyReplaysDamagePreventionReplacementForAttackDamage()
  {
    var attacker = new EntityInstance(
      "entity-attacker",
      "card-attacker",
      "player-1",
      EntityType.Minion,
      Attack: 3,
      Health: 3,
      Damage: 0);
    var defendingHero = new EntityInstance(
      "hero-player-2",
      "hero-card-player-2",
      "player-2",
      EntityType.Hero,
      Attack: 0,
      Health: 30,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Hero = defendingHero,
          Modifiers =
          [
            new PlayerModifier(
              "prevent-attack-damage",
              PlayerModifierTypes.DamagePrevention,
              Amount: 2,
              PlayerModifierDurations.NextDamage,
              Priority: 10,
              "effect:prevent")
          ]
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defendingHero.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState))
    ]);
    var replay = CreateReplayer().Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = CreateService();

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Equal(1, replay.State.Players["player-2"].Hero?.Damage);
    Assert.Empty(replay.State.Players["player-2"].Modifiers);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.Category == EventCategory.Proposed
      && @event.EventType == CommittedEventType.DamageWouldBeDealt);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.Category == EventCategory.Resolver
      && @event.EventType == CommittedEventType.ReplacementResolved);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.EntityDamaged);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.ModifierExpired);
  }

  [Fact]
  public void VerifyReplaysDeathrattleAfterDeathCleanup()
  {
    var attacker = new EntityInstance(
      "entity-attacker",
      "card-attacker",
      "player-1",
      EntityType.Minion,
      Attack: 3,
      Health: 3,
      Damage: 0);
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
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(board: [attacker])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(
            deck: [deckCard],
            setAside: [defenderCard],
            board: [defender])
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetPlayerId"] = "player-2",
          ["targetEntityInstanceId"] = defender.EntityInstanceId
        },
        expectedPreStateHash: hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateDeathrattleDrawCardVersion(defenderCard.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Empty(replay.State.Players["player-2"].Zones.Board);
    Assert.Contains(replay.State.Players["player-2"].Zones.Graveyard, card =>
      card.CardInstanceId == defenderCard.CardInstanceId);
    Assert.Contains(replay.State.Players["player-2"].Zones.Hand, card =>
      card.CardInstanceId == deckCard.CardInstanceId);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.EntityDied);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.DeathrattleTriggered);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.CardDrawn);
  }

  [Fact]
  public void VerifyReplaysQuestProgressAndCompletionReward()
  {
    var playedCard = new CardInstance(
      "card-instance-quest-progress",
      "card:river-guard",
      "card_river_guard_v1",
      "player-1",
      CardType.MinionCard);
    var questSource = new CardInstance(
      "quest-source-instance",
      "card:quest-draw",
      "card_quest_draw_v1",
      "player-1",
      CardType.SpellCard);
    var rewardCard = new CardInstance(
      "card-reward-drawn",
      "card:reward",
      "card_reward_v1",
      "player-1",
      CardType.MinionCard);
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
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Mana = 1,
          MaxMana = 1,
          Quests = [quest],
          Zones = new PlayerZones(
            deck: [rewardCard],
            hand: [playedCard],
            setAside: [questSource])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1,
          ["manaCost"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = playedCard.CardInstanceId,
          ["entityInstanceId"] = "entity-quest-progress"
        },
        hasher.HashGameplayState(initialState))
    ]);
    var catalogCards = new[] { CreateQuestRewardDrawCardVersion(questSource.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.True(Assert.Single(replay.State.Players["player-1"].Quests ?? []).IsCompleted);
    Assert.Contains(replay.State.Players["player-1"].Zones.Hand, card =>
      card.CardInstanceId == rewardCard.CardInstanceId);
    var progressEvent = Assert.Single(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.QuestProgressChanged);
    Assert.Contains(replay.DerivedEvents, @event =>
      @event.EventType == CommittedEventType.CardDrawn
      && @event.ParentEventSeq == progressEvent.EventSeq);
  }

  [Fact]
  public void VerifyReplaysSecretRevealAndReconstruction()
  {
    var secretCard = new CardInstance(
      "secret-card",
      "card:secret-draw",
      "secret-draw-v1",
      "player-1",
      CardType.SpellCard);
    var drawCard = new CardInstance(
      "draw-card",
      "card:draw",
      "draw-v1",
      "player-1",
      CardType.SpellCard);
    var opponentCard = new CardInstance(
      "opponent-minion-card",
      "card:opponent-minion",
      "opponent-minion-v1",
      "player-2",
      CardType.MinionCard);
    var secret = new PlayerSecret(
      "secret:player-1:secret-card:effect_secret_draw",
      secretCard.CardInstanceId,
      secretCard.CardVersion,
      "player-1",
      "MINION_SUMMONED",
      "effect_secret_draw",
      IsRevealed: false);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(deck: [drawCard], secretZone: [secretCard]),
          Secrets = [secret]
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Mana = 10,
          MaxMana = 10,
          Zones = new PlayerZones(hand: [opponentCard])
        }
      },
      activePlayerId: "player-2",
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-2",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 1,
          ["health"] = 1
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = opponentCard.CardInstanceId,
          ["entityInstanceId"] = "opponent-minion"
        })
    ]);
    var catalogCards = new[] { CreateSecretDrawCardVersion(secretCard.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.SecretRevealed);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.CardDrawn);
    Assert.Empty(replay.State.Players["player-1"].Zones.SecretZone);
    Assert.Single(replay.State.Players["player-1"].Zones.Hand);
    Assert.True(Assert.Single(replay.State.Players["player-1"].Secrets ?? []).IsRevealed);

    var reconstruction = new MatchReconstructionService().Reconstruct(new MatchReconstructionRequest(
      "match-secret-replay",
      commandLog,
      replay.DerivedEvents,
      replay.State,
      expectedFinalHash));
    Assert.Contains(
      reconstruction.Turns.SelectMany(turn => turn.Commands).SelectMany(command => command.Events),
      @event => @event.EventType == CommittedEventType.SecretRevealed);
  }

  [Fact]
  public void VerifyReplaysAuraRefreshAndReconstruction()
  {
    var auraCard = new CardInstance(
      "aura-source-card",
      "card:aura-source",
      "aura-source-v1",
      "player-1",
      CardType.MinionCard);
    var auraSource = new EntityInstance(
      "aura-source",
      auraCard.CardInstanceId,
      "player-1",
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(setAside: [auraCard], board: [auraSource])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      phase: "TURN_START");
    var commandLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "aura-refresh")
    ]);
    var catalogCards = new[] { CreateAuraCardVersion(auraCard.CardVersion) };
    var replay = CreateReplayer(catalogCards).Replay(initialState, commandLog);
    var expectedFinalHash = hasher.HashGameplayState(replay.State);
    var service = new ReplayVerificationService(CreateReplayer(catalogCards), hasher);

    var result = service.Verify(new ReplayVerificationRequest(
      initialState,
      commandLog,
      expectedFinalHash,
      hasher.HashGameplayState(initialState)));

    Assert.True(result.IsVerified);
    Assert.Contains(replay.DerivedEvents, @event => @event.EventType == CommittedEventType.AuraRefreshed);
    Assert.Single(replay.State.Players["player-1"].Auras ?? []);

    var reconstruction = new MatchReconstructionService().Reconstruct(new MatchReconstructionRequest(
      "match-aura-replay",
      commandLog,
      replay.DerivedEvents,
      replay.State,
      expectedFinalHash));
    Assert.Contains(
      reconstruction.Turns.SelectMany(turn => turn.Commands).SelectMany(command => command.Events),
      @event => @event.EventType == CommittedEventType.AuraRefreshed);
  }

  private ReplayVerificationService CreateService() => new(CreateReplayer(), hasher);

  private CommandLogReplayer CreateReplayer(IReadOnlyList<CardVersion>? catalogCards = null) =>
    new(new StateMutator(), new DerivedEventLog(), hasher, catalogCards);

  private static CardVersion CreateRandomTargetCardVersion()
  {
    var effect = new EffectDefinition(
      "effect_random_damage",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      new TargetSpec(
        Required: true,
        TargetSelectionMode.Random,
        new EntityQuery(
          "OPPONENT",
          [SourceZone.Board],
          [EntityType.Minion],
          RequiredTags: [],
          ExcludedTags: [],
          AliveOnly: true),
        new CountSpec(1, 1),
        "FIZZLE"),
      [
        new DealDamageActionDefinition(
          "action_random_damage",
          new ValueExpression("FIXED", 1, Ref: null),
          new TargetRef("targetRequirement"),
          DamageSchool: null,
          Lifesteal: false,
          Preventable: true)
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: true),
      Tags: ["RANDOM"]);
    var definition = new CardDefinition(
      "card_random_bolt",
      "card_random_bolt_v1",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Random Bolt",
        CardType.MinionCard,
        ["mage"],
        Rarity.Common,
        1,
        [],
        [],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 1, [], [], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Battlecry: Deal 1 damage to a random enemy minion.", FlavorText: null),
      new BalanceMetadata(1, 1, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card_random_bolt",
      "card_random_bolt_v1",
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateRandomSelectBrawlCardVersion()
  {
    var effect = new EffectDefinition(
      "effect_brawl",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new RandomSelectActionDefinition(
          "action_select_survivor",
          new EntityQuery(
            "ANY",
            [SourceZone.Board],
            [EntityType.Minion],
            RequiredTags: [],
            ExcludedTags: [],
            AliveOnly: true),
          new CountSpec(1, 1),
          "survivor",
          "RANDOM_TARGET",
          "FIZZLE_WHOLE_EFFECT"),
        new BatchDestroyActionDefinition(
          "action_destroy_others",
          new TargetRef("ALL_MINIONS_EXCEPT(ACTION_RESULT:action_select_survivor)"),
          "SIMULTANEOUS",
          "SKIP_INVALID_TARGETS")
      ],
      new ResolutionPolicy("BATCH_RESOLUTION"),
      new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: true),
      Tags: ["RANDOM"]);
    var definition = new CardDefinition(
      "card_brawl",
      "card_brawl_v1",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Controlled Brawl",
        CardType.MinionCard,
        ["warrior"],
        Rarity.Rare,
        1,
        [],
        [],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 1, [], [], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Battlecry: Randomly choose a minion. Destroy all others.", FlavorText: null),
      new BalanceMetadata(4, 2, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-27T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card_brawl",
      "card_brawl_v1",
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-27T00:00:00Z"),
      definition);
  }

  private static CardVersion CreateHistoryResurrectCardVersion(
    string ordering = "LAST",
    string historyQueryKind = "OWN_GRAVEYARD_MINION",
    PlayerSelector historyQueryPlayer = PlayerSelector.Self)
  {
    var effect = new EffectDefinition(
      "effect_history_resurrect",
      EffectKind.SimplePlayEffect,
      [SourceZone.Hand],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      TargetRequirement: null,
      [
        new ResurrectActionDefinition(
          "action_history_resurrect",
          new TargetRef("HISTORY_QUERY"),
          historyQueryPlayer,
          new ValueExpression("FIXED", 2, Ref: null),
          new ValueExpression("FIXED", 3, Ref: null),
          "APPEND",
          "SKIP_ACTION",
          ResultRef: "resurrected",
          new HistoryQuerySpec(
            historyQueryKind,
            historyQueryPlayer,
            ordering,
            MaxCandidates: 10))
      ],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["GRAVEYARD_RESURRECT"]);
    var definition = new CardDefinition(
      "card_resurrector",
      "card_resurrector_v1",
      "dsl-v1",
      "engine-v1",
      "primitive-v1",
      "ruleset_mvp_core_v1",
      "catalog-alpha",
      new StaticCardData(
        "Grave Caller",
        CardType.MinionCard,
        ["priest"],
        Rarity.Rare,
        1,
        [],
        [],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 1, [], [], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Battlecry: Resurrect your last dead minion.", FlavorText: null),
      new BalanceMetadata(4, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card_resurrector",
      "card_resurrector_v1",
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
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
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

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
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

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
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

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
      new StaticCardData(
        "Hidden Lesson",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["SECRET"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("SECRET", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Secret: When your opponent summons a minion, draw a card.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:secret-draw",
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
      new StaticCardData(
        "Battle Chant",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["BOARD_ATTACK_MODIFIER"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("SPELL", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Give your minions +2 Attack.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:board-attack-buff",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
      definition);
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
      new StaticCardData(
        "Splash Volley",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["DAMAGE_DISTRIBUTION"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("SPELL", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Deal 3 damage split across enemy minions in source order.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:damage-distribution",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
      definition);
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
      new StaticCardData(
        "Shield Chant",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["BOARD_KEYWORD_MODIFIER"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("SPELL", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Give your minions Taunt.", FlavorText: null),
      new BalanceMetadata(2, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:board-keyword-buff",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
      definition);
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
      new StaticCardData(
        "Grave Recall",
        CardType.SpellCard,
        ["neutral"],
        Rarity.Common,
        1,
        [],
        ["HAND_ZONE_MODIFIER"],
        true,
        ["mvp"]),
      new EntityDefinition(
        Minion: null,
        Spell: new SpellDefinition("SPELL", RequiresTarget: false),
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Move your hand to your graveyard.", FlavorText: null),
      new BalanceMetadata(4, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:hand-zone-modifier",
      cardVersionId,
      "dsl-v1",
      "engine-v1",
      "catalog-alpha",
      "pipeline-v1",
      "ruleset_mvp_core_v1",
      "sha256:ruleset",
      DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
      definition);
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
      new StaticCardData(
        "Banner Keeper",
        CardType.MinionCard,
        ["neutral"],
        Rarity.Common,
        2,
        [],
        ["AURA"],
        true,
        ["mvp"]),
      new EntityDefinition(
        new MinionDefinition(1, 3, [], ["AURA"], CanAttack: true),
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: null),
      [effect],
      new CardTextGeneration("Aura source.", FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(AssetManifestId: null, VisualAssetRef: null, AudioManifestId: null),
      new AuthoringMetadata(
        "test",
        DateTimeOffset.Parse("2026-06-30T00:00:00Z"),
        "manual-fixture",
        ["tests/Game.Application.Tests/ReplayVerificationServiceTests.cs"]));

    return new CardVersion(
      "card:aura-source",
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

  private static EntityInstance CreateReplayMinion(string entityInstanceId, string playerId) =>
    new(
      entityInstanceId,
      $"card:{entityInstanceId}",
      playerId,
      EntityType.Minion,
      Attack: 1,
      Health: 3,
      Damage: 0);
}
