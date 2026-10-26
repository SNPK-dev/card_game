using Game.Domain.Commands;

namespace Game.Application.Commands;

public sealed class GameCommandEnvelope
{
  public GameCommandEnvelope(
    string commandId,
    DateTimeOffset receivedAt,
    DateTimeOffset? clientTimestamp,
    string? correlationId,
    string? authSessionId,
    IReadOnlyDictionary<string, string>? transportMetadata,
    string? rawRequestRef,
    int commandSeq,
    string? playerId,
    string commandType,
    IReadOnlyDictionary<string, int>? payload = null,
    IReadOnlyDictionary<string, string>? refs = null,
    string? expectedPreStateHash = null,
    CommandCategory commandCategory = CommandCategory.Player,
    bool debugReplayFlag = false)
  {
    CommandId = commandId;
    ReceivedAt = receivedAt;
    ClientTimestamp = clientTimestamp;
    CorrelationId = correlationId;
    AuthSessionId = authSessionId;
    var sortedTransportMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in transportMetadata ?? EmptyTransportMetadata.Value)
    {
      sortedTransportMetadata[pair.Key] = pair.Value;
    }

    TransportMetadata = sortedTransportMetadata;
    RawRequestRef = rawRequestRef;
    CommandSeq = commandSeq;
    PlayerId = playerId;
    CommandType = commandType;
    var sortedPayload = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in payload ?? EmptyPayload.Value)
    {
      sortedPayload[pair.Key] = pair.Value;
    }

    Payload = sortedPayload;
    var sortedRefs = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in refs ?? EmptyRefs.Value)
    {
      sortedRefs[pair.Key] = pair.Value;
    }

    Refs = sortedRefs;
    ExpectedPreStateHash = expectedPreStateHash;
    CommandCategory = commandCategory;
    DebugReplayFlag = debugReplayFlag;
  }

  public string CommandId { get; }

  public DateTimeOffset ReceivedAt { get; }

  public DateTimeOffset? ClientTimestamp { get; }

  public string? CorrelationId { get; }

  public string? AuthSessionId { get; }

  public IReadOnlyDictionary<string, string> TransportMetadata { get; }

  public string? RawRequestRef { get; }

  public int CommandSeq { get; }

  public string? PlayerId { get; }

  public string CommandType { get; }

  public IReadOnlyDictionary<string, int> Payload { get; }

  public IReadOnlyDictionary<string, string> Refs { get; }

  public string? ExpectedPreStateHash { get; }

  public CommandCategory CommandCategory { get; }

  public bool DebugReplayFlag { get; }

  public DeterministicCommandPayload ToDeterministicPayload()
  {
    return new DeterministicCommandPayload(
      CommandSeq,
      PlayerId,
      CommandType,
      Payload,
      Refs,
      ExpectedPreStateHash,
      CommandCategory,
      DebugReplayFlag);
  }

  private static class EmptyTransportMetadata
  {
    internal static readonly IReadOnlyDictionary<string, string> Value =
      new SortedDictionary<string, string>(StringComparer.Ordinal);
  }

  private static class EmptyPayload
  {
    internal static readonly IReadOnlyDictionary<string, int> Value =
      new SortedDictionary<string, int>(StringComparer.Ordinal);
  }

  private static class EmptyRefs
  {
    internal static readonly IReadOnlyDictionary<string, string> Value =
      new SortedDictionary<string, string>(StringComparer.Ordinal);
  }
}
