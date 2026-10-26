using System.Text;
using Game.Application.Execution;
using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Randomness;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Xunit;

namespace Game.Domain.Tests;

public sealed class DeterministicSmokeTests
{
  private readonly ICanonicalSerializer _serializer = new CanonicalJsonSerializer();
  private readonly IStateHasher _hasher;

  public DeterministicSmokeTests()
  {
    _hasher = new StateHasher(_serializer);
  }

  [Fact]
  public void SameCommandLogProducesIdenticalFinalStateHash()
  {
    IGameCommand[] commands =
    [
      new NoOpCommand("draw"),
      new NoOpCommand("play"),
      new NoOpCommand("draw")
    ];

    var first = Replay(commands);
    var second = Replay(commands);

    Assert.Equal(_hasher.HashGameplayState(first), _hasher.HashGameplayState(second));
    Assert.Equal(3, first.Revision);
  }

  [Fact]
  public void SameCanonicalBytesProduceSameHash()
  {
    var bytes = Encoding.UTF8.GetBytes("""{"a":1,"b":2}""");

    Assert.Equal(_hasher.HashCanonicalBytes(bytes), _hasher.HashCanonicalBytes(bytes));
  }

  [Fact]
  public void CanonicalSerializationHasStableFieldOrdering()
  {
    var first = new Dictionary<string, int>
    {
      ["z"] = 1,
      ["a"] = 2
    };
    var second = new Dictionary<string, int>
    {
      ["a"] = 2,
      ["z"] = 1
    };

    var firstJson = Encoding.UTF8.GetString(_serializer.SerializeToCanonicalBytes(first));
    var secondJson = Encoding.UTF8.GetString(_serializer.SerializeToCanonicalBytes(second));

    Assert.Equal("""{"a":2,"z":1}""", firstJson);
    Assert.Equal(firstJson, secondJson);
  }

  [Fact]
  public void EventLogIsDerivedAndDoesNotMutateState()
  {
    var state = new InMemoryGameState();
    var next = new StateMutator().Apply(state, new NoOpCommand("draw"));
    var beforeHash = _hasher.HashGameplayState(state);

    var events = new DerivedEventLog().Derive(state, next, new NoOpCommand("draw"));

    Assert.Single(events);
    Assert.Equal(beforeHash, _hasher.HashGameplayState(state));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void PresentationMetadataDoesNotAffectGameplayStateHash()
  {
    var baseState = new InMemoryGameState().WithCounter("life", 20);
    var decoratedState = baseState.WithPresentationMetadata("card-art", "variant-a");

    Assert.Equal(_hasher.HashGameplayState(baseState), _hasher.HashGameplayState(decoratedState));
  }

  [Fact]
  public void GameplayRandomStreamIsSeparatedFromPresentationRandomStream()
  {
    IGameplayRandomStream gameplay = new GameplayRandomStream(42);
    IPresentationRandomStream presentation = new PresentationRandomStream(42);

    Assert.NotEqual(gameplay.NextUInt32(), presentation.NextUInt32());
  }

  [Fact]
  public void StateMutationPassesThroughStateMutator()
  {
    var mutator = new CountingMutator();
    var executor = new DeterministicCommandExecutor(mutator, new DerivedEventLog());

    var result = executor.Execute(new InMemoryGameState(), new NoOpCommand("play"));

    Assert.Equal(1, mutator.ApplyCallCount);
    Assert.Equal(1, result.State.Revision);
  }

  [Fact]
  public void StateMutatorAppliesProposedMutationBatchAndRecordsPostStateHash()
  {
    var state = new InMemoryGameState();
    var batch = CreateCounterBatch(state, "batch-001", "counter:draw", "draw");
    var mutator = new StateMutator();

    var application = mutator.Apply(state, batch);

    Assert.Equal(1, application.State.Revision);
    Assert.Equal(1, application.State.Counters["counter:draw"]);
    Assert.Equal(batch.PreStateHash, application.AppliedBatch.PreStateHash);
    Assert.Equal(_hasher.HashGameplayState(application.State), application.AppliedBatch.PostStateHash);
    Assert.Single(application.AppliedBatch.AppliedMutations);
    Assert.Empty(application.AppliedBatch.SkippedMutations);
  }

  [Fact]
  public void ProposedMutationBatchDoesNotCarryPostStateHash()
  {
    var propertyNames = typeof(ProposedMutationBatch)
      .GetProperties()
      .Select(property => property.Name)
      .ToArray();

    Assert.DoesNotContain("PostStateHash", propertyNames);
  }

  [Fact]
  public void StateMutatorRejectsMismatchedBatchPreStateHash()
  {
    var state = new InMemoryGameState();
    var batch = CreateCounterBatch(state, "batch-001", "counter:draw", "draw") with
    {
      PreStateHash = "not-current-state"
    };

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void StateMutatorCanonicalizesMutationOrder()
  {
    var state = new InMemoryGameState();
    var first = CreateCounterMutation("mutation-b", "counter:b", "b", "002");
    var second = CreateCounterMutation("mutation-a", "counter:a", "a", "001");
    var unorderedBatch = CreateBatch(state, "batch-unordered", [first, second]);
    var orderedBatch = CreateBatch(state, "batch-ordered", [second, first]);
    var mutator = new StateMutator();

    var unordered = mutator.Apply(state, unorderedBatch);
    var ordered = mutator.Apply(state, orderedBatch);

    Assert.Equal(_hasher.HashGameplayState(ordered.State), _hasher.HashGameplayState(unordered.State));
    Assert.Equal(["mutation-a", "mutation-b"], unordered.AppliedBatch.AppliedMutations.Select(mutation => mutation.MutationId));
  }

  [Fact]
  public void StateMutationHandlerRegistryRejectsDuplicateHandlers()
  {
    Assert.Throws<InvalidOperationException>(() =>
      new StateMutationHandlerRegistry(
      [
        new IncrementCounterMutationHandler(),
        new IncrementCounterMutationHandler()
      ]));
  }

  [Fact]
  public void StateMutatorRejectsUnsupportedMutationType()
  {
    var state = new InMemoryGameState();
    var mutation = CreateCounterMutation("mutation-unsupported", "counter:x", "x", "001") with
    {
      MutationType = "UNKNOWN_MUTATION"
    };
    var batch = CreateBatch(state, "batch-unsupported", [mutation]);
    var mutator = new StateMutator(
      new StateMutationHandlerRegistry([new IncrementCounterMutationHandler()]),
      _hasher);

    Assert.Throws<InvalidOperationException>(() => mutator.Apply(state, batch));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void StateMutatorRejectsPresentationOnlyMutationType()
  {
    var state = new InMemoryGameState();
    var mutation = CreateCounterMutation("mutation-audio", "counter:x", "x", "001") with
    {
      MutationType = "PLAY_AUDIO"
    };
    var batch = CreateBatch(state, "batch-audio", [mutation]);

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void ExecutorDoesNotMutateGameStateDirectly()
  {
    var original = new InMemoryGameState();
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    var result = executor.Execute(original, new NoOpCommand("play"));

    Assert.Equal(0, original.Revision);
    Assert.Equal(1, result.State.Revision);
  }

  [Fact]
  public void DeterministicExecutionNeedsNoExternalAccess()
  {
    var result = Replay([new NoOpCommand("draw"), new NoOpCommand("play")]);

    Assert.Equal(2, result.Revision);
    Assert.NotEmpty(_hasher.HashGameplayState(result));
  }

  [Fact]
  public void CanonicalCommandLogReplaysCommandsByCommandSequence()
  {
    var outOfOrderLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(2, "player-1", "play"),
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(3, "player-1", "draw")
    ]);
    var orderedLog = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(2, "player-1", "play"),
      new DeterministicCommandPayload(3, "player-1", "draw")
    ]);

    var first = Replay(outOfOrderLog);
    var second = Replay(orderedLog);

    Assert.Equal(_hasher.HashGameplayState(first.State), _hasher.HashGameplayState(second.State));
    Assert.Equal(3, first.State.Revision);
    Assert.Equal(2, first.State.Counters["command:draw"]);
    Assert.Equal(1, first.State.Counters["command:play"]);
  }

  [Theory]
  [MemberData(nameof(InvalidCommandLogs))]
  public void CanonicalCommandLogRejectsInvalidCommandSequences(
    DeterministicCommandPayload[] payloads)
  {
    Assert.Throws<ArgumentException>(() => new CanonicalCommandLog(payloads));
  }

  [Fact]
  public void ReplayRegeneratesCommittedEventsFromCommandPayloads()
  {
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", "draw"),
      new DeterministicCommandPayload(2, "player-1", "play")
    ]);

    var replay = Replay(log);

    Assert.Equal(2, replay.DerivedEvents.Count);
    Assert.Equal(
      [CommittedEventType.ModifierApplied, CommittedEventType.ModifierApplied],
      replay.DerivedEvents.Select(committed => committed.EventType));
    Assert.Equal([1, 2], replay.DerivedEvents.Select(committed => committed.EventSeq));
  }

  [Fact]
  public void ReplayChecksExpectedPreStateHashWhenPresent()
  {
    var initialState = new InMemoryGameState();
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        "draw",
        expectedPreStateHash: _hasher.HashGameplayState(initialState))
    ]);

    var replay = Replay(log);

    Assert.Equal(1, replay.State.Revision);
  }

  [Fact]
  public void ReplayRejectsMismatchedExpectedPreStateHash()
  {
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        "draw",
        expectedPreStateHash: "not-the-current-state")
    ]);

    Assert.Throws<InvalidOperationException>(() => Replay(log));
  }

  [Fact]
  public void DeterministicCommandPayloadDefaultsToPlayerCommandCategory()
  {
    var payload = new DeterministicCommandPayload(1, "player-1", "draw");

    Assert.Equal(CommandCategory.Player, payload.CommandCategory);
    Assert.False(payload.DebugReplayFlag);
  }

  [Fact]
  public void ReplayRejectsDebugCommandWithoutDebugReplayFlag()
  {
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "admin",
        "SET_STATE",
        commandCategory: CommandCategory.Debug)
    ]);

    Assert.Throws<InvalidOperationException>(() => Replay(log));
  }

  [Fact]
  public void ReplayAcceptsDebugCommandWithDebugReplayFlag()
  {
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "admin",
        "SET_STATE",
        commandCategory: CommandCategory.Debug,
        debugReplayFlag: true)
    ]);

    var replay = Replay(log);

    Assert.Equal(1, replay.State.Revision);
    Assert.Equal(CommittedEventType.ModifierApplied, replay.DerivedEvents.Single().EventType);
  }

  [Fact]
  public void ReplayAppliesEndTurnCommandsDeterministically()
  {
    var initialState = CreateTwoPlayerTurnState("player-1", turnNumber: 1);
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(1, "player-1", EndTurnCommand.CommandKind),
      new DeterministicCommandPayload(2, "player-2", EndTurnCommand.CommandKind)
    ]);

    var first = Replay(initialState, log);
    var second = Replay(initialState, log);

    Assert.Equal(_hasher.HashGameplayState(first.State), _hasher.HashGameplayState(second.State));
    Assert.Equal("player-1", first.State.ActivePlayerId);
    Assert.Equal(2, first.State.TurnNumber);
  }

  [Fact]
  public void ReplayAppliesPlayCardPayloadRefsDeterministically()
  {
    var card = new CardInstance(
      "card-instance-1",
      "card:river-guard",
      "1.0.0",
      "player-1",
      Game.Shared.Primitives.CardType.MinionCard);
    var initialState = new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(hand: [card])
        },
        ["player-2"] = PlayerState.Empty("player-2")
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        PlayCardCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["attack"] = 2,
          ["health"] = 3
        },
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = card.CardInstanceId,
          ["entityInstanceId"] = "entity-1"
        })
    ]);

    var first = Replay(initialState, log);
    var second = Replay(initialState, log);

    Assert.Equal(_hasher.HashGameplayState(first.State), _hasher.HashGameplayState(second.State));
    Assert.Empty(first.State.Players["player-1"].Zones.Hand);
    Assert.Equal(card.CardInstanceId, first.State.Players["player-1"].Zones.SetAside.Single().CardInstanceId);
    Assert.Equal("entity-1", first.State.Players["player-1"].Zones.Board.Single().EntityInstanceId);
  }

  [Fact]
  public void ReplayAppliesAttackPayloadRefsDeterministically()
  {
    var attacker = new EntityInstance(
      "entity-attacker",
      "card:attacker",
      "player-1",
      Game.Shared.Primitives.EntityType.Minion,
      Attack: 3,
      Health: 2,
      Damage: 0);
    var hero = new EntityInstance(
      "hero-player-2",
      "hero-card:player-2",
      "player-2",
      Game.Shared.Primitives.EntityType.Hero,
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
          Hero = hero
        }
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attacker.EntityInstanceId,
          ["targetEntityInstanceId"] = hero.EntityInstanceId,
          ["targetPlayerId"] = "player-2"
        })
    ]);

    var first = Replay(initialState, log);
    var second = Replay(initialState, log);

    Assert.Equal(_hasher.HashGameplayState(first.State), _hasher.HashGameplayState(second.State));
    Assert.Equal(3, first.State.Players["player-2"].Hero?.Damage);
  }

  [Fact]
  public void ReplayAppliesResolveChoicePayloadRefsDeterministically()
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
    var initialState = new InMemoryGameState(
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
    var log = new CanonicalCommandLog(
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        ResolveChoiceCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["cardInstanceId"] = "card-discovered-1",
          ["cardType"] = "SPELL_CARD",
          ["cardVersion"] = "card:arcane-b:v1",
          ["choiceId"] = choice.ChoiceId,
          ["selectedOptionId"] = "choice-spell:option:0002"
        },
        commandCategory: CommandCategory.Choice)
    ]);

    var first = Replay(initialState, log);
    var second = Replay(initialState, log);

    Assert.Equal(_hasher.HashGameplayState(first.State), _hasher.HashGameplayState(second.State));
    Assert.Empty(first.State.PendingChoices);
    Assert.Equal("card:arcane-b", first.State.Players["player-1"].Zones.Hand.Single().CardId);
    Assert.Equal(
      [CommittedEventType.ChoiceResolved, CommittedEventType.CardMovedZone],
      first.DerivedEvents.Select(@event => @event.EventType));
  }

  private IGameState Replay(IEnumerable<IGameCommand> commands)
  {
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());
    IGameState state = new InMemoryGameState();

    foreach (var command in commands)
    {
      state = executor.Execute(state, command).State;
    }

    return state;
  }

  private CommandLogReplayResult Replay(CanonicalCommandLog commandLog)
  {
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);
    return replayer.Replay(new InMemoryGameState(), commandLog);
  }

  private CommandLogReplayResult Replay(IGameState initialState, CanonicalCommandLog commandLog)
  {
    var replayer = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher);
    return replayer.Replay(initialState, commandLog);
  }

  private static InMemoryGameState CreateTwoPlayerTurnState(string activePlayerId, int turnNumber)
  {
    var player1Card = new CardInstance(
      "player-1-card-1",
      "card:test",
      "1.0.0",
      "player-1",
      Game.Shared.Primitives.CardType.MinionCard);
    var player2Card = new CardInstance(
      "player-2-card-1",
      "card:test",
      "1.0.0",
      "player-2",
      Game.Shared.Primitives.CardType.MinionCard);

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        ["player-1"] = PlayerState.Empty("player-1") with
        {
          Zones = new PlayerZones(deck: [player1Card])
        },
        ["player-2"] = PlayerState.Empty("player-2") with
        {
          Zones = new PlayerZones(deck: [player2Card])
        }
      },
      activePlayerId: activePlayerId,
      turnNumber: turnNumber,
      phase: "TURN_START");
  }

  private ProposedMutationBatch CreateCounterBatch(
    IGameState state,
    string batchId,
    string counterKey,
    string orderingKey)
  {
    return CreateBatch(
      state,
      batchId,
      [CreateCounterMutation($"mutation:{counterKey}", counterKey, "test", orderingKey)]);
  }

  private ProposedMutationBatch CreateBatch(
    IGameState state,
    string batchId,
    IReadOnlyList<StateMutation> mutations)
  {
    return new ProposedMutationBatch(
      batchId,
      state.Revision + 1,
      null,
      "TEST",
      "test",
      "TEST_BATCH",
      mutations,
      _hasher.HashGameplayState(state),
      null,
      null);
  }

  private static StateMutation CreateCounterMutation(
    string mutationId,
    string counterKey,
    string sourceRef,
    string orderingKey)
  {
    return new StateMutation(
      mutationId,
      IncrementCounterMutationHandler.SupportedMutationType,
      sourceRef,
      counterKey,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "TEST",
      1,
      null,
      orderingKey);
  }

  public static IEnumerable<object[]> InvalidCommandLogs()
  {
    yield return
    [
      new[]
      {
        new DeterministicCommandPayload(1, "player-1", "draw"),
        new DeterministicCommandPayload(1, "player-1", "play")
      }
    ];

    yield return
    [
      new[]
      {
        new DeterministicCommandPayload(2, "player-1", "draw")
      }
    ];
  }

  private sealed class CountingMutator : IStateMutator
  {
    private readonly StateMutator _inner = new();

    public int ApplyCallCount { get; private set; }

    public IGameState Apply(IGameState state, IGameCommand command)
    {
      ApplyCallCount++;
      return _inner.Apply(state, command);
    }

    public StateMutationApplication Apply(IGameState state, ProposedMutationBatch batch)
    {
      ApplyCallCount++;
      return _inner.Apply(state, batch);
    }
  }
}
