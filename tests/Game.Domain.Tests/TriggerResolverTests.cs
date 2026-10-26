using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Domain.Triggers;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Domain.Tests;

public sealed class TriggerResolverTests
{
  [Fact]
  public void ResolveReturnsMatchesInCanonicalOrder()
  {
    var resolver = new TriggerResolver();
    var laterEvent = CreateEvent(9, CommittedEventType.CardPlayed, playerId: "player-1");
    var earlierEvent = CreateEvent(7, CommittedEventType.CardPlayed, playerId: "player-1");
    var lateTrigger = CreateRegistration("trigger-b", "effect-b", EventType.CardPlayed, "player-1", ["02"]);
    var earlyTrigger = CreateRegistration("trigger-a", "effect-a", EventType.CardPlayed, "player-1", ["01"]);

    var result = resolver.Resolve(new TriggerResolutionRequest(
      [laterEvent, earlierEvent],
      [lateTrigger, earlyTrigger]));

    Assert.True(result.IsAccepted);
    Assert.Equal(
      [
        "trigger-match:0000000007:trigger-a",
        "trigger-match:0000000007:trigger-b",
        "trigger-match:0000000009:trigger-a",
        "trigger-match:0000000009:trigger-b"
      ],
      result.Matches.Select(match => match.MatchId));
  }

  [Fact]
  public void ResolveHonorsControllerScope()
  {
    var resolver = new TriggerResolver();
    var selfTrigger = CreateRegistration("trigger-self", "effect-self", EventType.CardDrawn, "player-1", ["01"], "SELF");
    var opponentTrigger = CreateRegistration("trigger-opponent", "effect-opponent", EventType.CardDrawn, "player-1", ["02"], "OPPONENT");

    var result = resolver.Resolve(new TriggerResolutionRequest(
      [CreateEvent(7, CommittedEventType.CardDrawn, playerId: "player-2")],
      [selfTrigger, opponentTrigger]));

    var match = Assert.Single(result.Matches);
    Assert.Equal("trigger-opponent", match.TriggerId);
  }

  [Fact]
  public void ResolveRejectsUnsupportedFiltersAndScopesExplicitly()
  {
    var resolver = new TriggerResolver();
    var filteredTrigger = CreateRegistration(
      "trigger-filter",
      "effect-filter",
      EventType.CardPlayed,
      "player-1",
      ["01"],
      eventFilterJson: """{"cardType":"SPELL"}""");
    var scopedTrigger = CreateRegistration(
      "trigger-scope",
      "effect-scope",
      EventType.CardPlayed,
      "player-1",
      ["02"],
      controllerScope: "OWNER_ONLY");

    var result = resolver.Resolve(new TriggerResolutionRequest(
      [CreateEvent(7, CommittedEventType.CardPlayed)],
      [filteredTrigger, scopedTrigger]));

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == TriggerResolutionDiagnosticCode.UnsupportedEventFilter &&
      diagnostic.TriggerId == "trigger-filter");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == TriggerResolutionDiagnosticCode.UnsupportedControllerScope &&
      diagnostic.TriggerId == "trigger-scope");
  }

  [Fact]
  public void PresentationEventsDoNotTriggerGameplayAndStateIsUnchanged()
  {
    var state = new InMemoryGameState().WithCounter("life", 20);
    var hasher = new StateHasher(new CanonicalJsonSerializer());
    var before = hasher.HashGameplayState(state);
    var resolver = new TriggerResolver();
    var trigger = CreateRegistration("trigger-a", "effect-a", EventType.CardPlayed, "player-1", ["01"]);

    var result = resolver.Resolve(new TriggerResolutionRequest(
      [CreateEvent(7, CommittedEventType.CardPlayed, EventCategory.Presentation)],
      [trigger]));

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Matches);
    Assert.Equal(before, hasher.HashGameplayState(state));
    Assert.Equal(1, state.Revision);
  }

  private static TriggerRegistration CreateRegistration(
    string triggerId,
    string effectId,
    EventType eventType,
    string controllerPlayerId,
    IReadOnlyList<string> orderingKeys,
    string controllerScope = "ANY",
    string? eventFilterJson = null) =>
    new(
      triggerId,
      effectId,
      new TriggerDefinition(
        eventType,
        eventFilterJson is null
          ? null
          : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(eventFilterJson),
        controllerScope,
        OncePerEvent: true),
      controllerPlayerId,
      $"entity:{triggerId}",
      orderingKeys);

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
}
