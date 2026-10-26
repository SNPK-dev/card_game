using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Replacements;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class CommittedEventFactoryTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void IncrementCounterMutationCreatesCommittedEventWithBatchAndHashRefs()
  {
    var state = new InMemoryGameState();
    var batch = CreateBatch(
      state,
      "batch-counter",
      [
        CreateCounterMutation("mutation-counter", "counter:draw", "001")
      ]);
    var application = new StateMutator().Apply(state, batch);

    var events = new CommittedEventFactory()
      .CreateCommittedEvents(application.AppliedBatch, firstEventSeq: 7);

    var committed = Assert.Single(events);
    Assert.Equal(7, committed.EventSeq);
    Assert.Equal(CommittedEventType.ModifierApplied, committed.EventType);
    Assert.Equal(EventCategory.Committed, committed.Category);
    Assert.Equal(batch.BatchId, committed.MutationBatchRef);
    Assert.Equal(["mutation-counter"], committed.MutationRefs);
    Assert.Equal(batch.CommandSeq, committed.CommandSeq);
    Assert.Equal(batch.PreStateHash, committed.PreStateHash);
    Assert.Equal(application.AppliedBatch.PostStateHash, committed.PostStateHash);
    Assert.Contains(committed.Payload, entry => entry.Key == "amount" && entry.Value.IntValue == 1);
    Assert.Contains(committed.Payload, entry => entry.Key == "mutationType" && entry.Value.StringValue == "INCREMENT_COUNTER");
  }

  [Fact]
  public void MoveCardZoneMutationCreatesCardMovedZoneCommittedEvent()
  {
    var card = new CardInstance(
      "card-instance-1",
      "card:river-crocolisk",
      "1.0.0",
      "player-1",
      CardType.MinionCard);
    var state = CreateState(new PlayerZones(deck: [card]));
    var batch = CreateBatch(
      state,
      "batch-move",
      [
        CreateMoveCardMutation("mutation-move", card.CardInstanceId, "001")
      ]);
    var application = new StateMutator().Apply(state, batch);

    var events = new CommittedEventFactory()
      .CreateCommittedEvents(application.AppliedBatch, firstEventSeq: 20);

    var committed = Assert.Single(events);
    Assert.Equal(CommittedEventType.CardMovedZone, committed.EventType);
    Assert.Equal(20, committed.EventSeq);
    Assert.Contains(committed.TargetRefs, target => target.RefType == "CARD_INSTANCE" && target.RefId == card.CardInstanceId);
    Assert.Contains(committed.Payload, entry => entry.Key == "fromZone" && entry.Value.StringValue == "DECK");
    Assert.Contains(committed.Payload, entry => entry.Key == "toZone" && entry.Value.StringValue == "HAND");
    Assert.Contains(committed.Payload, entry => entry.Key == "playerId" && entry.Value.StringValue == "player-1");
  }

  [Theory]
  [InlineData(DrawCardMutationHandler.SupportedMutationType, CommittedEventType.CardDrawn, "CARD_INSTANCE")]
  [InlineData(SummonEntityMutationHandler.SupportedMutationType, CommittedEventType.MinionSummoned, "ENTITY_INSTANCE")]
  [InlineData(ApplyDamageMutationHandler.SupportedMutationType, CommittedEventType.EntityDamaged, "ENTITY_INSTANCE")]
  [InlineData(HealEntityMutationHandler.SupportedMutationType, CommittedEventType.EntityHealed, "ENTITY_INSTANCE")]
  [InlineData(ModifyManaMutationHandler.SupportedMutationType, CommittedEventType.ManaChanged, "PLAYER")]
  [InlineData(ModifyFatigueMutationHandler.SupportedMutationType, CommittedEventType.ModifierApplied, "PLAYER")]
  [InlineData(DestroyEntityMutationHandler.SupportedMutationType, CommittedEventType.EntityDestroyed, "ENTITY_INSTANCE")]
  [InlineData(ReturnEntityToHandMutationHandler.SupportedMutationType, CommittedEventType.CardReturnedToHand, "ENTITY_INSTANCE")]
  public void BasicGameplayMutationsCreateCommittedEvents(
    string mutationType,
    CommittedEventType expectedEventType,
    string expectedTargetRefType)
  {
    var appliedBatch = new AppliedMutationBatch(
      "batch-basic",
      1,
      null,
      "ON_PLAY",
      "test",
      "TEST",
      [
        new StateMutation(
          "mutation-basic",
          mutationType,
          "test",
          "target-1",
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["amount"] = 1
          },
          "ON_PLAY",
          1,
          null,
          "001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["playerId"] = "player-1"
          })
      ],
      [],
      "sha256:pre",
      "sha256:post",
      [],
      1);

    var committed = new CommittedEventFactory()
      .CreateCommittedEvents(appliedBatch, firstEventSeq: 1)
      .Single();

    Assert.Equal(expectedEventType, committed.EventType);
    Assert.Contains(committed.TargetRefs, target =>
      target.RefType == expectedTargetRefType && target.RefId == "target-1");
    Assert.Contains(committed.Payload, entry =>
      entry.Key == "mutationType" && entry.Value.StringValue == mutationType);
  }

  [Fact]
  public void CommittedEventFactoryPreservesAppliedMutationOrderForEventSequences()
  {
    var state = new InMemoryGameState();
    var batch = CreateBatch(
      state,
      "batch-ordered",
      [
        CreateCounterMutation("mutation-b", "counter:b", "002"),
        CreateCounterMutation("mutation-a", "counter:a", "001")
      ]);
    var application = new StateMutator().Apply(state, batch);

    var events = new CommittedEventFactory()
      .CreateCommittedEvents(application.AppliedBatch, firstEventSeq: 30);

    Assert.Equal(["mutation-a", "mutation-b"], events.SelectMany(ev => ev.MutationRefs));
    Assert.Equal([30, 31], events.Select(ev => ev.EventSeq));
  }

  [Fact]
  public void CommittedEventFactoryEmitsProposedAndResolverEventsBeforeCommittedMutation()
  {
    var damageMutation = new StateMutation(
      "mutation-damage",
      ApplyDamageMutationHandler.SupportedMutationType,
      "source-card",
      "entity-target",
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 3
      },
      "ON_PLAY",
      4,
      null,
      "001",
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = "entity-target",
        ["playerId"] = "player-2"
      });
    var proposed = new ProposedEventRecord(
      "proposed:mutation-damage:damage",
      EventSeqCandidate: 10,
      CommandSeq: 4,
      ParentEventSeq: null,
      Phase.OnPlay,
      ProposedEventType.DamageWouldBeDealt,
      [new EventRef("SOURCE", "source-card", null, null)],
      [new EventRef("ENTITY_INSTANCE", "entity-target", null, "player-2")],
      damageMutation.MutationId,
      [
        new EventPayloadEntry(
          "amount",
          new EventPayloadValue("INT", null, 3, null, []))
      ],
      "replacement:damage:mvp",
      PreventionAllowed: true,
      RngRollRefs: [],
      EventVisibility.DebugOnly);
    var decision = new ReplacementDecision(
      "replacement-decision:0000000010:proposed:mutation-damage:damage",
      proposed.EventId,
      proposed.EventSeqCandidate,
      proposed.ProposedEventType,
      ReplacementDecisionResult.Modified,
      "DAMAGE_PREVENTED:1");
    var appliedBatch = new AppliedMutationBatch(
      "batch-replacement",
      4,
      null,
      "ON_PLAY",
      "source-card",
      "TEST",
      [damageMutation],
      [],
      "sha256:pre",
      "sha256:post",
      [],
      4,
      [proposed],
      [decision]);

    var events = new CommittedEventFactory().CreateCommittedEvents(appliedBatch, firstEventSeq: 10);

    Assert.Equal(
      [CommittedEventType.DamageWouldBeDealt, CommittedEventType.ReplacementResolved, CommittedEventType.EntityDamaged],
      events.Select(@event => @event.EventType));
    Assert.Equal([EventCategory.Proposed, EventCategory.Resolver, EventCategory.Committed], events.Select(@event => @event.Category));
    Assert.Equal([10, 11, 12], events.Select(@event => @event.EventSeq));
    Assert.Contains(events[0].Payload, payload =>
      payload.Key == "replacementWindow" && payload.Value.StringValue == "replacement:damage:mvp");
    Assert.Contains(events[1].Payload, payload =>
      payload.Key == "result" && payload.Value.StringValue == ReplacementDecisionResult.Modified.ToString());
  }

  [Fact]
  public void CommittedEventFactoryRejectsUnmappedMutationTypes()
  {
    var appliedBatch = new AppliedMutationBatch(
      "batch-unknown",
      1,
      null,
      "ON_PLAY",
      "test",
      "TEST",
      [
        new StateMutation(
          "mutation-unknown",
          "UNKNOWN_GAMEPLAY_MUTATION",
          "test",
          "target",
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "ON_PLAY",
          1,
          null,
          "001")
      ],
      [],
      "sha256:pre",
      "sha256:post",
      [],
      1);

    Assert.Throws<InvalidOperationException>(() =>
      new CommittedEventFactory().CreateCommittedEvents(appliedBatch, firstEventSeq: 1));
  }

  [Fact]
  public void PresentationMutationIsRejectedBeforeCommittedEventCreation()
  {
    var state = new InMemoryGameState();
    var mutation = CreateCounterMutation("mutation-audio", "counter:x", "001") with
    {
      MutationType = "PLAY_AUDIO"
    };
    var batch = CreateBatch(state, "batch-audio", [mutation]);

    Assert.Throws<InvalidOperationException>(() => new StateMutator().Apply(state, batch));
  }

  [Fact]
  public void CommandReplayStillDoesNotRequirePersistedCommittedEvents()
  {
    var replay = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher)
      .Replay(
        new InMemoryGameState(),
        new CanonicalCommandLog(
        [
          new DeterministicCommandPayload(1, "player-1", "draw")
        ]));

    Assert.Equal(1, replay.State.Revision);
    Assert.Equal(CommittedEventType.ModifierApplied, replay.DerivedEvents.Single().EventType);
    Assert.Equal(1, replay.DerivedEvents.Single().EventSeq);
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

  private static StateMutation CreateMoveCardMutation(
    string mutationId,
    string cardInstanceId,
    string orderingKey)
  {
    return new StateMutation(
      mutationId,
      MoveCardZoneMutationHandler.SupportedMutationType,
      "test",
      cardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      "ON_PLAY",
      1,
      null,
      orderingKey,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = cardInstanceId,
        ["fromZone"] = "DECK",
        ["playerId"] = "player-1",
        ["toZone"] = "HAND"
      });
  }

  private static InMemoryGameState CreateState(PlayerZones zones)
  {
    var player = PlayerState.Empty("player-1") with
    {
      Zones = zones
    };

    return new InMemoryGameState(
      players: new SortedDictionary<string, PlayerState>(StringComparer.Ordinal)
      {
        [player.PlayerId] = player
      });
  }
}
