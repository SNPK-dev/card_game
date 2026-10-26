using System.Security.Cryptography;
using Game.Domain.Serialization;
using Game.Shared.Events;

namespace Game.Domain.Hashing;

public sealed class CommittedEventLogHasher(ICanonicalSerializer serializer) : ICommittedEventLogHasher
{
  public string HashGameplayEvents(IEnumerable<CommittedEventRecord> events)
  {
    ArgumentNullException.ThrowIfNull(events);

    var projection = events
      .Where(@event => @event.Category != EventCategory.Presentation)
      .OrderBy(@event => @event.EventSeq)
      .Select(CreateProjection)
      .ToArray();

    return HashCanonicalBytes(serializer.SerializeToCanonicalBytes(projection));
  }

  private static CommittedEventHashProjection CreateProjection(CommittedEventRecord committedEvent) =>
    new(
      committedEvent.EventSeq,
      committedEvent.ParentEventSeq,
      committedEvent.CommandSeq,
      committedEvent.EventType.ToString(),
      committedEvent.Category.ToString(),
      committedEvent.Phase.ToString(),
      committedEvent.SourceRefs
        .OrderBy(eventRef => eventRef.RefType, StringComparer.Ordinal)
        .ThenBy(eventRef => eventRef.RefId, StringComparer.Ordinal)
        .Select(CreateRefProjection)
        .ToArray(),
      committedEvent.TargetRefs
        .OrderBy(eventRef => eventRef.RefType, StringComparer.Ordinal)
        .ThenBy(eventRef => eventRef.RefId, StringComparer.Ordinal)
        .Select(CreateRefProjection)
        .ToArray(),
      committedEvent.Payload
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .ThenBy(entry => entry.Value.ValueKind, StringComparer.Ordinal)
        .Select(CreatePayloadProjection)
        .ToArray(),
      committedEvent.MutationBatchRef,
      committedEvent.MutationRefs.Order(StringComparer.Ordinal).ToArray(),
      committedEvent.RngRollRefs.Order(StringComparer.Ordinal).ToArray(),
      committedEvent.PreStateHash,
      committedEvent.PostStateHash,
      committedEvent.Visibility.ToString());

  private static EventRefHashProjection CreateRefProjection(EventRef eventRef) =>
    new(
      eventRef.RefType,
      eventRef.RefId,
      eventRef.Zone,
      eventRef.ControllerPlayerId);

  private static EventPayloadHashProjection CreatePayloadProjection(EventPayloadEntry entry) =>
    new(
      entry.Key,
      entry.Value.ValueKind,
      entry.Value.StringValue,
      entry.Value.IntValue,
      entry.Value.BoolValue,
      entry.Value.StringValues.Order(StringComparer.Ordinal).ToArray());

  private static string HashCanonicalBytes(byte[] canonicalBytes)
  {
    var hash = SHA256.HashData(canonicalBytes);
    return Convert.ToHexString(hash);
  }

  private sealed record CommittedEventHashProjection(
    long EventSeq,
    long? ParentEventSeq,
    long? CommandSeq,
    string EventType,
    string Category,
    string Phase,
    IReadOnlyList<EventRefHashProjection> SourceRefs,
    IReadOnlyList<EventRefHashProjection> TargetRefs,
    IReadOnlyList<EventPayloadHashProjection> Payload,
    string? MutationBatchRef,
    IReadOnlyList<string> MutationRefs,
    IReadOnlyList<string> RngRollRefs,
    string PreStateHash,
    string PostStateHash,
    string Visibility);

  private sealed record EventRefHashProjection(
    string RefType,
    string RefId,
    string? Zone,
    string? ControllerPlayerId);

  private sealed record EventPayloadHashProjection(
    string Key,
    string ValueKind,
    string? StringValue,
    int? IntValue,
    bool? BoolValue,
    IReadOnlyList<string> StringValues);
}
