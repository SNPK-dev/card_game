using Game.Application.Commands;
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

namespace Game.Application.Tests;

public sealed class CommandEnvelopeTests
{
  private readonly ICommandHasher _commandHasher = new CommandHasher(new CanonicalJsonSerializer());
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void EnvelopeProjectsOnlyDeterministicReplayFields()
  {
    var first = CreateEnvelope(
      commandId: "request-a",
      receivedAt: new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
      correlationId: "correlation-a",
      authSessionId: "session-a",
      rawRequestRef: "raw-a");
    var second = CreateEnvelope(
      commandId: "request-b",
      receivedAt: new DateTimeOffset(2026, 6, 23, 10, 1, 0, TimeSpan.Zero),
      correlationId: "correlation-b",
      authSessionId: "session-b",
      rawRequestRef: "raw-b");

    var firstPayload = first.ToDeterministicPayload();
    var secondPayload = second.ToDeterministicPayload();

    Assert.Equal(firstPayload.CommandSeq, secondPayload.CommandSeq);
    Assert.Equal(firstPayload.PlayerId, secondPayload.PlayerId);
    Assert.Equal(firstPayload.CommandType, secondPayload.CommandType);
    Assert.Equal(firstPayload.Payload, secondPayload.Payload);
    Assert.Equal(firstPayload.Refs, secondPayload.Refs);
    Assert.Equal(firstPayload.ExpectedPreStateHash, secondPayload.ExpectedPreStateHash);
    Assert.Equal(firstPayload.CommandCategory, secondPayload.CommandCategory);
    Assert.Equal(firstPayload.DebugReplayFlag, secondPayload.DebugReplayFlag);

    var deterministicFieldNames = typeof(Game.Domain.Commands.DeterministicCommandPayload)
      .GetProperties()
      .Select(property => property.Name)
      .ToArray();
    Assert.DoesNotContain("CommandId", deterministicFieldNames);
    Assert.DoesNotContain("ReceivedAt", deterministicFieldNames);
    Assert.DoesNotContain("ClientTimestamp", deterministicFieldNames);
    Assert.DoesNotContain("CorrelationId", deterministicFieldNames);
    Assert.DoesNotContain("AuthSessionId", deterministicFieldNames);
    Assert.DoesNotContain("TransportMetadata", deterministicFieldNames);
    Assert.DoesNotContain("RawRequestRef", deterministicFieldNames);
  }

  [Fact]
  public void EnvelopeMetadataDoesNotAffectReplayOutput()
  {
    var firstLog = new CanonicalCommandLog(
    [
      CreateEnvelope("request-a", new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero))
        .ToDeterministicPayload()
    ]);
    var secondLog = new CanonicalCommandLog(
    [
      CreateEnvelope("request-b", new DateTimeOffset(2026, 6, 23, 10, 1, 0, TimeSpan.Zero))
        .ToDeterministicPayload()
    ]);

    var firstReplay = Replay(firstLog);
    var secondReplay = Replay(secondLog);

    Assert.Equal(
      _hasher.HashGameplayState(firstReplay.State),
      _hasher.HashGameplayState(secondReplay.State));
    Assert.Equal(CommittedEventType.ModifierApplied, firstReplay.DerivedEvents.Single().EventType);
    Assert.Equal(CommittedEventType.ModifierApplied, secondReplay.DerivedEvents.Single().EventType);
    Assert.Equal(firstReplay.DerivedEvents.Single().Payload, secondReplay.DerivedEvents.Single().Payload);
  }

  [Fact]
  public void EnvelopeMetadataDoesNotAffectCommandHash()
  {
    var first = CreateEnvelope(
      commandId: "request-a",
      receivedAt: new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
      correlationId: "correlation-a",
      authSessionId: "session-a",
      rawRequestRef: "raw-a");
    var second = CreateEnvelope(
      commandId: "request-b",
      receivedAt: new DateTimeOffset(2026, 6, 23, 10, 1, 0, TimeSpan.Zero),
      correlationId: "correlation-b",
      authSessionId: "session-b",
      rawRequestRef: "raw-b");

    Assert.Equal(
      _commandHasher.HashCommand(first.ToDeterministicPayload()),
      _commandHasher.HashCommand(second.ToDeterministicPayload()));
  }

  [Fact]
  public void EnvelopeProjectsDebugReplayClassification()
  {
    var envelope = CreateEnvelope(
      "debug-request",
      new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
      commandCategory: CommandCategory.Debug,
      debugReplayFlag: true);

    var payload = envelope.ToDeterministicPayload();

    Assert.Equal(CommandCategory.Debug, payload.CommandCategory);
    Assert.True(payload.DebugReplayFlag);
  }

  [Fact]
  public void EnvelopeProjectsDeterministicStringRefs()
  {
    var envelope = CreateEnvelope(
      "play-request",
      new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["entityInstanceId"] = "entity-1",
        ["cardInstanceId"] = "card-instance-1"
      });

    var payload = envelope.ToDeterministicPayload();

    Assert.Equal(["cardInstanceId", "entityInstanceId"], payload.Refs.Keys);
    Assert.Equal("card-instance-1", payload.Refs["cardInstanceId"]);
    Assert.Equal("entity-1", payload.Refs["entityInstanceId"]);
  }

  [Fact]
  public void EnvelopeProjectsFullDeterministicPayloadSurface()
  {
    var envelope = CreateEnvelope(
      "play-request",
      new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
      refs: new Dictionary<string, string>
      {
        ["entityInstanceId"] = "entity-1",
        ["cardInstanceId"] = "card-instance-1"
      },
      payload: new Dictionary<string, int>
      {
        ["manaCost"] = 2,
        ["attack"] = 3
      },
      expectedPreStateHash: "pre-state-hash",
      commandCategory: CommandCategory.Choice,
      debugReplayFlag: true);

    var payload = envelope.ToDeterministicPayload();

    Assert.Equal(1, payload.CommandSeq);
    Assert.Equal("player-1", payload.PlayerId);
    Assert.Equal("draw", payload.CommandType);
    Assert.Equal(["attack", "manaCost"], payload.Payload.Keys);
    Assert.Equal(3, payload.Payload["attack"]);
    Assert.Equal(2, payload.Payload["manaCost"]);
    Assert.Equal(["cardInstanceId", "entityInstanceId"], payload.Refs.Keys);
    Assert.Equal("pre-state-hash", payload.ExpectedPreStateHash);
    Assert.Equal(CommandCategory.Choice, payload.CommandCategory);
    Assert.True(payload.DebugReplayFlag);
  }

  private CommandLogReplayResult Replay(CanonicalCommandLog commandLog)
  {
    return new CommandLogReplayer(new StateMutator(), new DerivedEventLog(), _hasher)
      .Replay(new InMemoryGameState(), commandLog);
  }

  private static GameCommandEnvelope CreateEnvelope(
    string commandId,
    DateTimeOffset receivedAt,
    string correlationId = "correlation",
    string authSessionId = "session",
    string rawRequestRef = "raw",
    IReadOnlyDictionary<string, string>? refs = null,
    IReadOnlyDictionary<string, int>? payload = null,
    string? expectedPreStateHash = null,
    CommandCategory commandCategory = CommandCategory.Player,
    bool debugReplayFlag = false)
  {
    return new GameCommandEnvelope(
      commandId,
      receivedAt,
      receivedAt.AddSeconds(-1),
      correlationId,
      authSessionId,
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["remote"] = commandId,
        ["transport"] = "test"
      },
      rawRequestRef,
      1,
      "player-1",
      "draw",
      payload ?? new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = 1
      },
      refs,
      expectedPreStateHash,
      commandCategory,
      debugReplayFlag);
  }
}
