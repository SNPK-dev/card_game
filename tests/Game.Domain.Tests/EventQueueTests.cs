using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;
using Xunit;

namespace Game.Domain.Tests;

public sealed class EventQueueTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void RootEventsDrainInStableFifoOrderWithStrictEventSequences()
  {
    var queue = new EventQueue(nextEventSeq: 10);
    queue.EnqueueRoot(CreateEvent("event-a", "CARD_PLAYED"));
    queue.EnqueueRoot(CreateEvent("event-b", "ENTITY_DAMAGED"));
    queue.EnqueueRoot(CreateEvent("event-c", "ENTITY_DIED"));

    var drained = queue.Drain();

    Assert.Equal(["event-a", "event-b", "event-c"], drained.Select(item => item.Event.EventId));
    Assert.Equal([10, 11, 12], drained.Select(item => item.EventSeq));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public void ChildEventsDrainByCanonicalOrderingKeysWithStableTieBreak()
  {
    var queue = new EventQueue();
    var firstTie = CreateEvent("event-tie-1", "TRIGGER_MATCHED", parentEventSeq: 4, orderingKeys: ["02", "A"]);
    var secondTie = CreateEvent("event-tie-2", "TRIGGER_MATCHED", parentEventSeq: 4, orderingKeys: ["02", "A"]);
    var earliest = CreateEvent("event-earliest", "TRIGGER_MATCHED", parentEventSeq: 4, orderingKeys: ["01", "Z"]);
    var latest = CreateEvent("event-latest", "TRIGGER_MATCHED", parentEventSeq: 4, orderingKeys: ["03"]);

    queue.EnqueueChildren([firstTie, latest, earliest, secondTie]);

    var drained = queue.Drain();

    Assert.Equal(
      ["event-earliest", "event-tie-1", "event-tie-2", "event-latest"],
      drained.Select(item => item.Event.EventId));
    Assert.All(drained, item => Assert.Equal(4, item.Event.ParentEventSeq));
  }

  [Fact]
  public void QueueRejectsEventCountOverflowBeforeAcceptingEvent()
  {
    var queue = new EventQueue(new EventQueueOptions(MaxEventDepth: 64, MaxQueuedEvents: 1));
    queue.EnqueueRoot(CreateEvent("event-a", "CARD_PLAYED"));

    Assert.Throws<InvalidOperationException>(() => queue.EnqueueRoot(CreateEvent("event-b", "CARD_DRAWN")));
    Assert.Equal(1, queue.Count);
  }

  [Fact]
  public void QueueRejectsEventDepthOverflow()
  {
    var queue = new EventQueue(new EventQueueOptions(MaxEventDepth: 1, MaxQueuedEvents: 10));

    Assert.Throws<InvalidOperationException>(() =>
      queue.EnqueueRoot(CreateEvent("event-too-deep", "ENTITY_DIED", depth: 2)));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public void QueueRejectsPresentationEvents()
  {
    var queue = new EventQueue();

    Assert.Throws<InvalidOperationException>(() =>
      queue.EnqueueRoot(CreateEvent("event-audio", "AUDIO_CUE", category: EventCategory.Presentation)));
    Assert.Equal(0, queue.Count);
  }

  [Fact]
  public void EventQueueDrainDoesNotMutateGameplayStateOrReplaceCommandReplay()
  {
    var state = new InMemoryGameState();
    var beforeHash = _hasher.HashGameplayState(state);
    var queue = new EventQueue();
    queue.EnqueueRoot(CreateEvent("event-card-played", "CARD_PLAYED"));

    var drained = queue.Drain();
    var replay = new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher)
      .Replay(
        state,
        new CanonicalCommandLog(
        [
          new DeterministicCommandPayload(1, "player-1", "draw")
        ]));

    Assert.Single(drained);
    Assert.Equal(beforeHash, _hasher.HashGameplayState(state));
    Assert.Equal(0, state.Revision);
    Assert.Equal(1, replay.State.Revision);
    Assert.Equal(CommittedEventType.ModifierApplied, replay.DerivedEvents.Single().EventType);
    Assert.Equal(1, replay.DerivedEvents.Single().EventSeq);
  }

  private static QueuedEvent CreateEvent(
    string eventId,
    string eventType,
    EventCategory category = EventCategory.Committed,
    int? parentEventSeq = null,
    IReadOnlyList<string>? orderingKeys = null,
    int depth = 0)
  {
    return new QueuedEvent(
      eventId,
      category,
      eventType,
      parentEventSeq,
      CommandSeq: 1,
      Phase: "ON_PLAY",
      SourceRef: "source:card-1",
      TargetRefs: ["target:entity-1"],
      CanonicalOrderingKeys: orderingKeys ?? [eventType, eventId],
      depth,
      EventVisibility.Public);
  }
}
