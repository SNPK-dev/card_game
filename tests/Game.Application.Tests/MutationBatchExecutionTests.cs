using Game.Application.Execution;
using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class MutationBatchExecutionTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void BatchExecutionReturnsStateAppliedBatchCommittedEventsAndProcessedEvents()
  {
    var state = new InMemoryGameState();
    var batch = CreateCounterBatch(state, "batch-draw", "counter:draw");
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 11);

    Assert.Equal(1, result.State.Revision);
    Assert.Equal(batch.BatchId, result.AppliedBatch.BatchId);
    Assert.Equal(batch.PreStateHash, result.AppliedBatch.PreStateHash);
    Assert.Equal(_hasher.HashGameplayState(result.State), result.AppliedBatch.PostStateHash);

    var committed = Assert.Single(result.CommittedEvents);
    Assert.Equal(11, committed.EventSeq);
    Assert.Equal(EventCategory.Committed, committed.Category);
    Assert.Equal(result.AppliedBatch.PostStateHash, committed.PostStateHash);

    var processed = Assert.Single(result.ProcessedEvents);
    Assert.Equal(committed.EventSeq, processed.EventSeq);
    Assert.Equal($"committed:{committed.EventSeq}", processed.Event.EventId);
  }

  [Fact]
  public void BatchExecutionAlignsCommittedAndProcessedEventSequencesForMultipleMutations()
  {
    var state = new InMemoryGameState();
    var batch = CreateBatch(
      state,
      "batch-two",
      [
        CreateCounterMutation("mutation-b", "counter:b", "002"),
        CreateCounterMutation("mutation-a", "counter:a", "001")
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 21);

    Assert.Equal([21, 22], result.CommittedEvents.Select(ev => ev.EventSeq));
    Assert.Equal([21, 22], result.ProcessedEvents.Select(ev => ev.EventSeq));
    Assert.Equal(["mutation-a", "mutation-b"], result.CommittedEvents.SelectMany(ev => ev.MutationRefs));
  }

  [Fact]
  public void BatchExecutionPropagatesMutationFailuresBeforeEventsAreCreated()
  {
    var state = new InMemoryGameState();
    var batch = CreateCounterBatch(state, "batch-bad", "counter:draw") with
    {
      PreStateHash = "not-current-state"
    };
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    Assert.Throws<InvalidOperationException>(() => executor.Execute(state, batch));
    Assert.Equal(0, state.Revision);
  }

  [Fact]
  public void BatchExecutionDoesNotReplaceCommandLogReplayInput()
  {
    var batchExecutor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());
    var state = new InMemoryGameState();
    var batch = CreateCounterBatch(state, "batch-draw", "counter:draw");

    var batchResult = batchExecutor.Execute(state, batch);
    var replay = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher)
      .Replay(
        new InMemoryGameState(),
        new CanonicalCommandLog(
        [
          new DeterministicCommandPayload(1, "player-1", "draw")
        ]));

    Assert.NotEmpty(batchResult.CommittedEvents);
    Assert.Equal(1, replay.State.Revision);
    Assert.Equal(CommittedEventType.ModifierApplied, replay.DerivedEvents.Single().EventType);
    Assert.Equal(1, replay.DerivedEvents.Single().EventSeq);
  }

  [Fact]
  public void BatchExecutionComposesBasicGameplayMutations()
  {
    var entity = new EntityInstance(
      "entity-1",
      "card-instance-1",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0);
    var state = CreateState(new PlayerZones(board: [entity]));
    var batch = CreateBatch(
      state,
      "batch-basic-actions",
      [
        new StateMutation(
          "mutation-damage",
          ApplyDamageMutationHandler.SupportedMutationType,
          "test",
          entity.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 2
          },
          "ON_PLAY",
          1,
          null,
          "001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = entity.EntityInstanceId,
            ["playerId"] = "player-1"
          }),
        new StateMutation(
          "mutation-heal",
          HealEntityMutationHandler.SupportedMutationType,
          "test",
          entity.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 1
          },
          "ON_PLAY",
          1,
          null,
          "002",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = entity.EntityInstanceId,
            ["playerId"] = "player-1"
          })
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 40);

    Assert.Equal(1, result.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Equal(
      [CommittedEventType.EntityDamaged, CommittedEventType.EntityHealed],
      result.CommittedEvents.Select(committed => committed.EventType));
    Assert.Equal([40, 41], result.ProcessedEvents.Select(processed => processed.EventSeq));
  }

  [Fact]
  public void BatchExecutionAppliesDamagePreventionBeforeCommittedDamageEvent()
  {
    var entity = new EntityInstance(
      "entity-1",
      "card-instance-1",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0);
    var state = CreateState(
      new PlayerZones(board: [entity]),
      [
        new PlayerModifier(
          "prevent-1",
          PlayerModifierTypes.DamagePrevention,
          Amount: 2,
          PlayerModifierDurations.NextDamage,
          Priority: 10,
          "effect:prevent")
      ]);
    var batch = CreateBatch(
      state,
      "batch-prevent-damage",
      [
        CreateDamageMutation(entity.EntityInstanceId, amount: 4, preventable: true)
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 42);

    Assert.Equal(2, result.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Empty(result.State.Players["player-1"].Modifiers);
    Assert.Equal(
      [
        CommittedEventType.DamageWouldBeDealt,
        CommittedEventType.ReplacementResolved,
        CommittedEventType.EntityDamaged,
        CommittedEventType.ModifierExpired
      ],
      result.CommittedEvents.Select(committed => committed.EventType));
    Assert.Equal(EventCategory.Proposed, result.CommittedEvents[0].Category);
    Assert.Equal(EventCategory.Resolver, result.CommittedEvents[1].Category);
    Assert.Equal(2, result.AppliedBatch.AppliedMutations.Single(mutation =>
      mutation.MutationType == ApplyDamageMutationHandler.SupportedMutationType).Payload["amount"]);
    Assert.Single(result.AppliedBatch.ProposedEvents);
    Assert.Single(result.AppliedBatch.ReplacementDecisions);
  }

  [Fact]
  public void BatchExecutionCancelsDamageWhenPreventionCoversFullAmount()
  {
    var entity = new EntityInstance(
      "entity-1",
      "card-instance-1",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0);
    var state = CreateState(
      new PlayerZones(board: [entity]),
      [
        new PlayerModifier(
          "prevent-1",
          PlayerModifierTypes.DamagePrevention,
          Amount: 5,
          PlayerModifierDurations.NextDamage,
          Priority: 10,
          "effect:prevent")
      ]);
    var batch = CreateBatch(
      state,
      "batch-prevent-all-damage",
      [
        CreateDamageMutation(entity.EntityInstanceId, amount: 4, preventable: true)
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 44);

    Assert.Equal(0, result.State.Players["player-1"].Zones.Board.Single().Damage);
    Assert.Empty(result.State.Players["player-1"].Modifiers);
    Assert.Equal(
      [CommittedEventType.DamageWouldBeDealt, CommittedEventType.ReplacementResolved, CommittedEventType.ModifierExpired],
      result.CommittedEvents.Select(committed => committed.EventType));
    Assert.Equal(EventCategory.Proposed, result.CommittedEvents[0].Category);
    Assert.Equal(EventCategory.Resolver, result.CommittedEvents[1].Category);
    Assert.DoesNotContain(result.AppliedBatch.AppliedMutations, mutation =>
      mutation.MutationType == ApplyDamageMutationHandler.SupportedMutationType);
  }

  [Fact]
  public void BatchExecutionConsumesDivineShieldBeforeApplyingDamage()
  {
    var entity = new EntityInstance(
      "entity-1",
      "card-instance-1",
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 0,
      Keywords: ["DIVINE_SHIELD"]);
    var state = CreateState(
      new PlayerZones(board: [entity]),
      [
        new PlayerModifier(
          "prevent-1",
          PlayerModifierTypes.DamagePrevention,
          Amount: 5,
          PlayerModifierDurations.NextDamage,
          Priority: 10,
          "effect:prevent")
      ]);
    var batch = CreateBatch(
      state,
      "batch-divine-shield",
      [
        CreateDamageMutation(entity.EntityInstanceId, amount: 4, preventable: true)
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 46);

    var nextEntity = Assert.Single(result.State.Players["player-1"].Zones.Board);
    Assert.Equal(0, nextEntity.Damage);
    Assert.Null(nextEntity.Keywords);
    Assert.Single(result.State.Players["player-1"].Modifiers);
    Assert.Equal(
      [CommittedEventType.DamageWouldBeDealt, CommittedEventType.ReplacementResolved, CommittedEventType.ModifierExpired],
      result.CommittedEvents.Select(committed => committed.EventType));
    Assert.Equal(EventCategory.Proposed, result.CommittedEvents[0].Category);
    Assert.Equal(EventCategory.Resolver, result.CommittedEvents[1].Category);
    var mutation = Assert.Single(result.AppliedBatch.AppliedMutations);
    Assert.Equal(RemoveKeywordMutationHandler.SupportedMutationType, mutation.MutationType);
    Assert.Equal("DIVINE_SHIELD", mutation.Metadata!["keyword"]);
    Assert.DoesNotContain(result.AppliedBatch.AppliedMutations, applied =>
      applied.MutationType == ApplyDamageMutationHandler.SupportedMutationType);
  }

  [Fact]
  public void BatchExecutionDestroysBoardEntityAndMovesSourceCardToGraveyard()
  {
    var sourceCard = new CardInstance(
      "card-instance-1",
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
    var entity = new EntityInstance(
      "entity-1",
      sourceCard.CardInstanceId,
      "player-1",
      EntityType.Minion,
      Attack: 2,
      Health: 5,
      Damage: 5);
    var state = CreateState(new PlayerZones(setAside: [sourceCard], board: [entity]));
    var batch = CreateBatch(
      state,
      "batch-destroy",
      [
        new StateMutation(
          "mutation-destroy",
          DestroyEntityMutationHandler.SupportedMutationType,
          "test",
          entity.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "DEATHRATTLE_PHASE",
          1,
          null,
          "001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["entityInstanceId"] = entity.EntityInstanceId,
            ["playerId"] = "player-1"
          })
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 45);

    Assert.Empty(result.State.Players["player-1"].Zones.Board);
    Assert.Empty(result.State.Players["player-1"].Zones.SetAside);
    Assert.Equal(sourceCard.CardInstanceId, result.State.Players["player-1"].Zones.Graveyard.Single().CardInstanceId);
    Assert.Equal(CommittedEventType.EntityDestroyed, result.CommittedEvents.Single().EventType);
  }

  [Fact]
  public void BatchExecutionCreatesMovementAndSummonEventsForPlayCardMutations()
  {
    var card = new CardInstance(
      "card-instance-1",
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
    var state = CreateState(new PlayerZones(hand: [card]));
    var batch = new Game.Domain.Actions.PlayCardActionExecutor()
      .Execute(
        new Game.Domain.Actions.ActionExecutionContext(
          state,
          new PlayCardCommand("player-1", card.CardInstanceId, "entity-1", 2, 3),
          1,
          _hasher.HashGameplayState(state)));
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 50);

    Assert.Equal(
      [CommittedEventType.CardMovedZone, CommittedEventType.MinionSummoned, CommittedEventType.ModifierApplied],
      result.CommittedEvents.Select(committed => committed.EventType));
    Assert.Equal([50, 51, 52], result.ProcessedEvents.Select(processed => processed.EventSeq));
  }

  [Fact]
  public void BatchExecutionAppliesTerminalStateAndCreatesGameEndedEvent()
  {
    var state = new InMemoryGameState();
    var batch = CreateBatch(
      state,
      "batch-terminal",
      [
        new StateMutation(
          "mutation-terminal",
          SetTerminalStateMutationHandler.SupportedMutationType,
          "test",
          "game",
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "ON_PLAY",
          1,
          null,
          "001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["reason"] = "EMPTY_RESOURCES_DRAW",
            ["message"] = "All players have empty decks and hands after turn end."
          })
      ]);
    var executor = new DeterministicMutationBatchExecutor(
      new StateMutator(),
      new CommittedEventFactory());

    var result = executor.Execute(state, batch, firstEventSeq: 60);

    Assert.True(result.State.TerminalState.IsTerminal);
    Assert.Equal("EMPTY_RESOURCES_DRAW", result.State.TerminalState.Reason);
    var committed = Assert.Single(result.CommittedEvents);
    Assert.Equal(CommittedEventType.GameEnded, committed.EventType);
    Assert.Equal("SET_TERMINAL_STATE", Assert.Single(
      committed.Payload,
      entry => entry.Key == "mutationType").Value.StringValue);
  }

  private ProposedMutationBatch CreateCounterBatch(
    IGameState state,
    string batchId,
    string counterKey)
  {
    return CreateBatch(
      state,
      batchId,
      [CreateCounterMutation($"mutation:{counterKey}", counterKey, "001")]);
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
      "ON_PLAY",
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
    string orderingKey)
  {
    return new StateMutation(
      mutationId,
      IncrementCounterMutationHandler.SupportedMutationType,
      "test",
      counterKey,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      "ON_PLAY",
      1,
      null,
      orderingKey);
  }

  private static StateMutation CreateDamageMutation(
    string entityInstanceId,
    int amount,
    bool preventable)
  {
    return new StateMutation(
      $"mutation:damage:{entityInstanceId}",
      ApplyDamageMutationHandler.SupportedMutationType,
      "test",
      entityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      "ON_PLAY",
      1,
      null,
      "001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = entityInstanceId,
        ["playerId"] = "player-1",
        ["preventable"] = preventable.ToString(System.Globalization.CultureInfo.InvariantCulture)
      });
  }

  private static InMemoryGameState CreateState(
    PlayerZones zones,
    IReadOnlyList<PlayerModifier>? modifiers = null)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Zones = zones,
      Modifiers = modifiers ?? []
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player
      },
      activePlayerId: "player-1",
      turnNumber: 1,
      phase: "TURN_START");
  }
}
