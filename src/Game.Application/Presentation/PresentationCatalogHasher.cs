using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class PresentationCatalogHasher : IPresentationCatalogHasher
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
  {
    WriteIndented = false
  };

  public string HashManifest(AssetManifest manifest)
  {
    ArgumentNullException.ThrowIfNull(manifest);

    var visualManifest = manifest.VisualManifest is null
      ? null
      : new VisualManifestHashProjection(
        manifest.VisualManifest.VisualManifestId,
        manifest.VisualManifest.VisualManifestVersion,
        manifest.VisualManifest.AssetRef,
        manifest.VisualManifest.Format,
        manifest.VisualManifest.ContentHash);

    var audioManifest = manifest.AudioManifest is null
      ? null
      : new AudioManifestHashProjection(
        manifest.AudioManifest.AudioManifestId,
        manifest.AudioManifest.AudioManifestVersion,
        manifest.AudioManifest.AudioAssetMode.ToString(),
        manifest.AudioManifest.ContentHash,
        manifest.AudioManifest.FallbackCueRefs.Order(StringComparer.Ordinal).ToArray(),
        manifest.AudioManifest.Cues
          .OrderBy(cue => cue.CueId, StringComparer.Ordinal)
          .ThenBy(cue => cue.CueType.ToString(), StringComparer.Ordinal)
          .Select(cue => new AudioCueHashProjection(
            cue.CueId,
            cue.CueType.ToString(),
            cue.AssetRef,
            cue.DurationMs,
            cue.Language,
            cue.VolumeGroup,
            cue.FallbackCueId,
            cue.ContentHash))
          .ToArray());

    var projection = new PresentationManifestHashProjection(
      manifest.AssetManifestId,
      manifest.AssetManifestVersion,
      manifest.CardId,
      manifest.CardVersionId,
      manifest.VisualAssetMode.ToString(),
      manifest.AudioAssetMode.ToString(),
      manifest.ContentHash,
      visualManifest,
      audioManifest);

    var canonicalBytes = SerializeCanonical(projection);
    return Convert.ToHexString(SHA256.HashData(canonicalBytes));
  }

  private static byte[] SerializeCanonical<T>(T value)
  {
    var node = JsonSerializer.SerializeToNode(value, JsonOptions);
    var canonicalNode = SortNode(node);
    var json = canonicalNode?.ToJsonString(JsonOptions) ?? "null";
    return Encoding.UTF8.GetBytes(json);
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

  private sealed record PresentationManifestHashProjection(
    string AssetManifestId,
    string AssetManifestVersion,
    string CardId,
    string CardVersionId,
    string VisualAssetMode,
    string AudioAssetMode,
    string ContentHash,
    VisualManifestHashProjection? VisualManifest,
    AudioManifestHashProjection? AudioManifest);

  private sealed record VisualManifestHashProjection(
    string VisualManifestId,
    string VisualManifestVersion,
    string AssetRef,
    string Format,
    string ContentHash);

  private sealed record AudioManifestHashProjection(
    string AudioManifestId,
    string AudioManifestVersion,
    string AudioAssetMode,
    string ContentHash,
    IReadOnlyList<string> FallbackCueRefs,
    IReadOnlyList<AudioCueHashProjection> Cues);

  private sealed record AudioCueHashProjection(
    string CueId,
    string CueType,
    string AssetRef,
    int DurationMs,
    string? Language,
    string VolumeGroup,
    string? FallbackCueId,
    string ContentHash);
}
