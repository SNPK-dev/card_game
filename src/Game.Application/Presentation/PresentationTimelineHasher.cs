using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class PresentationTimelineHasher : IPresentationTimelineHasher
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
  {
    WriteIndented = false
  };

  public string HashTimeline(PresentationTimeline timeline)
  {
    ArgumentNullException.ThrowIfNull(timeline);

    var projection = new PresentationTimelineHashProjection(
      timeline.TimelineId,
      timeline.PresentationCatalogHash,
      timeline.FirstSourceEventSeq,
      timeline.LastSourceEventSeq,
      timeline.Entries
        .OrderBy(entry => entry.SourceEventSeq)
        .ThenBy(entry => entry.AssetManifestId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
        .Select(entry => new PresentationTimelineEntryHashProjection(
          entry.EntryId,
          entry.EntryKind.ToString(),
          entry.SourceEventSeq,
          entry.CueType.ToString(),
          entry.SourceCardId,
          entry.SourceCardVersionId,
          entry.AssetManifestId,
          entry.AssetRef,
          entry.VisibilityPolicy,
          entry.Reason))
        .ToArray());

    var node = JsonSerializer.SerializeToNode(projection, JsonOptions);
    var canonicalNode = SortNode(node);
    var json = canonicalNode?.ToJsonString(JsonOptions) ?? "null";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
  }

  private static JsonNode? SortNode(JsonNode? node)
  {
    return node switch
    {
      JsonObject jsonObject => SortObject(jsonObject),
      JsonArray jsonArray => SortArray(jsonArray),
      _ => node?.DeepClone()
    };
  }

  private static JsonObject SortObject(JsonObject jsonObject)
  {
    var sorted = new JsonObject();
    foreach (var property in jsonObject.OrderBy(item => item.Key, StringComparer.Ordinal))
    {
      sorted[property.Key] = SortNode(property.Value);
    }

    return sorted;
  }

  private static JsonArray SortArray(JsonArray jsonArray)
  {
    var sorted = new JsonArray();
    foreach (var item in jsonArray)
    {
      sorted.Add(SortNode(item));
    }

    return sorted;
  }

  private sealed record PresentationTimelineHashProjection(
    string TimelineId,
    string PresentationCatalogHash,
    long? FirstSourceEventSeq,
    long? LastSourceEventSeq,
    IReadOnlyList<PresentationTimelineEntryHashProjection> Entries);

  private sealed record PresentationTimelineEntryHashProjection(
    string EntryId,
    string EntryKind,
    long SourceEventSeq,
    string CueType,
    string? SourceCardId,
    string? SourceCardVersionId,
    string? AssetManifestId,
    string? AssetRef,
    string VisibilityPolicy,
    string Reason);
}
