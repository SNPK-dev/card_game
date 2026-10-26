using Game.Domain.History;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class HistoryStoreTests
{
  [Fact]
  public void AppendCommittedEventsStoresGameplayFactsInEventSeqOrder()
  {
    var store = new HistoryStore();
    var later = CreateEvent(9, CommittedEventType.EntityDamaged, playerId: "player-2");
    var earlier = CreateEvent(7, CommittedEventType.CardPlayed, playerId: "player-1");

    var next = store.AppendCommittedEvents([later, earlier]);

    Assert.Empty(store.Records);
    Assert.Equal([7, 9], next.Records.Select(record => record.EventSeq));
    Assert.Equal("history:0000000007:CardPlayed", next.Records[0].HistoryRecordId);
    Assert.Equal(CommittedEventType.CardPlayed, next.Records[0].EventType);
    Assert.Equal("player-1", next.Records[0].PlayerId);
    Assert.Contains(next.Records[0].Payload, entry => entry.Key == "playerId");
  }

  [Fact]
  public void AppendCommittedEventsExcludesPresentationRecords()
  {
    var store = new HistoryStore();
    var gameplay = CreateEvent(7, CommittedEventType.CardPlayed);
    var presentation = CreateEvent(8, CommittedEventType.CardPlayed, EventCategory.Presentation);

    var next = store.AppendCommittedEvents([presentation, gameplay]);

    var record = Assert.Single(next.Records);
    Assert.Equal(7, record.EventSeq);
    Assert.Equal(1, next.CountByType(CommittedEventType.CardPlayed));
  }

  [Fact]
  public void HistoryQueriesAreDeterministicByTypeAndPlayer()
  {
    var store = new HistoryStore().AppendCommittedEvents(
    [
      CreateEvent(3, CommittedEventType.CardDrawn, playerId: "player-1"),
      CreateEvent(1, CommittedEventType.CardDrawn, playerId: "player-2"),
      CreateEvent(2, CommittedEventType.CardDrawn, playerId: "player-1")
    ]);

    Assert.Equal(3, store.CountByType(CommittedEventType.CardDrawn));
    Assert.Equal(2, store.CountByTypeAndPlayer(CommittedEventType.CardDrawn, "player-1"));
    Assert.Equal([1, 2, 3], store.FindByType(CommittedEventType.CardDrawn).Select(record => record.EventSeq));
  }

  [Fact]
  public void FindDeadMinionsUsesEntityDiedPayloadInCommittedEventOrder()
  {
    var store = new HistoryStore().AppendCommittedEvents(
    [
      CreateEntityDiedEvent(5, "player-1", "card-dead-late", "2.0.0"),
      CreateEvent(2, CommittedEventType.CardPlayed, playerId: "player-1"),
      CreateEntityDiedEvent(3, "player-2", "card-opponent-dead", "1.0.0"),
      CreateEntityDiedEvent(1, "player-1", "card-dead-first", "1.0.0"),
      CreateEntityDiedEvent(4, "player-1", "card-presentation", "1.0.0", EventCategory.Presentation)
    ]);

    var all = store.FindDeadMinions();
    var own = store.FindDeadMinions("player-1", maxCandidates: 1);

    Assert.Equal(["card-dead-first", "card-opponent-dead", "card-dead-late"], all.Select(candidate => candidate.SourceCardInstanceId));
    Assert.Equal("2.0.0", all[2].SourceCardVersion);
    Assert.Equal(5, all[2].EventSeq);
    Assert.Equal(["card-dead-first"], own.Select(candidate => candidate.SourceCardInstanceId));
  }

  private static CommittedEventRecord CreateEvent(
    long eventSeq,
    CommittedEventType eventType,
    EventCategory category = EventCategory.Committed,
    string playerId = "player-1") =>
    new(
      eventSeq,
      null,
      1,
      eventType,
      category,
      Phase.OnPlay,
      [new EventRef("SOURCE", "source-1", "BOARD", playerId)],
      [new EventRef("ENTITY_INSTANCE", "entity-1", "BOARD", playerId)],
      [new EventPayloadEntry("playerId", new EventPayloadValue("STRING", playerId, null, null, []))],
      "batch-1",
      ["mutation-1"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);

  private static CommittedEventRecord CreateEntityDiedEvent(
    long eventSeq,
    string playerId,
    string sourceCardInstanceId,
    string cardVersion,
    EventCategory category = EventCategory.Committed) =>
    new(
      eventSeq,
      null,
      1,
      CommittedEventType.EntityDied,
      category,
      Phase.OnPlay,
      [new EventRef("SOURCE", sourceCardInstanceId, "BOARD", playerId)],
      [new EventRef("ENTITY_INSTANCE", $"entity:{sourceCardInstanceId}", "BOARD", playerId)],
      [
        new EventPayloadEntry("playerId", new EventPayloadValue("STRING", playerId, null, null, [])),
        new EventPayloadEntry("sourceCardInstanceId", new EventPayloadValue("STRING", sourceCardInstanceId, null, null, [])),
        new EventPayloadEntry("cardVersion", new EventPayloadValue("STRING", cardVersion, null, null, []))
      ],
      "batch-1",
      ["mutation-1"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);
}
