using Game.Domain.Commands;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class CanonicalHashingTests
{
  private readonly ICommandHasher _commandHasher = new CommandHasher(new CanonicalJsonSerializer());
  private readonly ICommittedEventLogHasher _eventLogHasher = new CommittedEventLogHasher(new CanonicalJsonSerializer());

  [Fact]
  public void CommandHashIsStableAcrossPayloadDictionaryOrdering()
  {
    var first = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      new Dictionary<string, int>
      {
        ["z"] = 2,
        ["a"] = 1
      });
    var second = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      new Dictionary<string, int>
      {
        ["a"] = 1,
        ["z"] = 2
      });

    Assert.Equal(_commandHasher.HashCommand(first), _commandHasher.HashCommand(second));
  }

  [Fact]
  public void CommandHashIsStableAcrossRefDictionaryOrdering()
  {
    var first = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      refs: new Dictionary<string, string>
      {
        ["entityInstanceId"] = "entity-1",
        ["cardInstanceId"] = "card-1"
      });
    var second = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      refs: new Dictionary<string, string>
      {
        ["cardInstanceId"] = "card-1",
        ["entityInstanceId"] = "entity-1"
      });

    Assert.Equal(_commandHasher.HashCommand(first), _commandHasher.HashCommand(second));
  }

  [Fact]
  public void CommandHashChangesWhenDeterministicCommandFieldChanges()
  {
    var first = new DeterministicCommandPayload(1, "player-1", "PLAY_CARD");
    var second = new DeterministicCommandPayload(1, "player-2", "PLAY_CARD");

    Assert.NotEqual(_commandHasher.HashCommand(first), _commandHasher.HashCommand(second));
  }

  [Fact]
  public void CommandHashChangesWhenDeterministicRefChanges()
  {
    var first = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      refs: new Dictionary<string, string>
      {
        ["cardInstanceId"] = "card-1",
        ["entityInstanceId"] = "entity-1"
      });
    var second = new DeterministicCommandPayload(
      1,
      "player-1",
      "PLAY_CARD",
      refs: new Dictionary<string, string>
      {
        ["cardInstanceId"] = "card-2",
        ["entityInstanceId"] = "entity-1"
      });

    Assert.NotEqual(_commandHasher.HashCommand(first), _commandHasher.HashCommand(second));
  }

  [Fact]
  public void GameplayEventLogHashIsStableAcrossNestedCollectionOrdering()
  {
    var first = CreateCommittedEvent(
      sourceRefs:
      [
        new EventRef("SOURCE", "b", null, null),
        new EventRef("SOURCE", "a", null, null)
      ],
      payload:
      [
        new EventPayloadEntry("z", new EventPayloadValue("STRING", "z-value", null, null, [])),
        new EventPayloadEntry("a", new EventPayloadValue("STRING", "a-value", null, null, []))
      ],
      mutationRefs: ["mutation-b", "mutation-a"],
      rngRollRefs: ["rng-b", "rng-a"]);
    var second = CreateCommittedEvent(
      sourceRefs:
      [
        new EventRef("SOURCE", "a", null, null),
        new EventRef("SOURCE", "b", null, null)
      ],
      payload:
      [
        new EventPayloadEntry("a", new EventPayloadValue("STRING", "a-value", null, null, [])),
        new EventPayloadEntry("z", new EventPayloadValue("STRING", "z-value", null, null, []))
      ],
      mutationRefs: ["mutation-a", "mutation-b"],
      rngRollRefs: ["rng-a", "rng-b"]);

    Assert.Equal(
      _eventLogHasher.HashGameplayEvents([first]),
      _eventLogHasher.HashGameplayEvents([second]));
  }

  [Fact]
  public void GameplayEventLogHashExcludesPresentationOnlyEvents()
  {
    var gameplayEvent = CreateCommittedEvent();
    var presentationEvent = gameplayEvent with
    {
      EventSeq = 2,
      Category = EventCategory.Presentation,
      EventType = CommittedEventType.CardPlayed,
      Payload =
      [
        new EventPayloadEntry("assetRef", new EventPayloadValue("STRING", "asset://audio/on-play.ogg", null, null, []))
      ]
    };

    Assert.Equal(
      _eventLogHasher.HashGameplayEvents([gameplayEvent]),
      _eventLogHasher.HashGameplayEvents([gameplayEvent, presentationEvent]));
  }

  private static CommittedEventRecord CreateCommittedEvent(
    IReadOnlyList<EventRef>? sourceRefs = null,
    IReadOnlyList<EventPayloadEntry>? payload = null,
    IReadOnlyList<string>? mutationRefs = null,
    IReadOnlyList<string>? rngRollRefs = null) =>
    new(
      1,
      null,
      1,
      CommittedEventType.EntityDamaged,
      EventCategory.Committed,
      Phase.OnPlay,
      sourceRefs ?? [new EventRef("SOURCE", "source-1", null, null)],
      [new EventRef("ENTITY_INSTANCE", "entity-1", "BOARD", "player-1")],
      payload ?? [new EventPayloadEntry("amount", new EventPayloadValue("INT", null, 3, null, []))],
      "batch-1",
      mutationRefs ?? ["mutation-1"],
      rngRollRefs ?? [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);
}
