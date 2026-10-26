using System.Security.Cryptography;
using System.Text;
using Game.Generator.Providers;
using Game.Shared.Assets;

namespace Game.Generator.Generation;

public sealed class GeneratedAssetManifestBuilder
{
  public AssetManifest BuildVisualManifest(
    string generationId,
    string cardId,
    string cardVersionId,
    VisualAssetMode visualMode,
    GeneratedAssetProviderResult asset,
    DateTimeOffset createdAt)
  {
    ArgumentNullException.ThrowIfNull(asset);

    var moderation = ToModerationStatus(asset.ModerationStatus);
    var visual = new VisualAssetManifest(
      $"visual:{generationId}:{visualMode}",
      "visual-manifest-v1",
      asset.AssetRef,
      asset.Format,
      moderation,
      asset.ContentHash);

    return new AssetManifest(
      $"asset:{generationId}:{visualMode}",
      "asset-manifest-bootstrap-0.1.0",
      cardId,
      cardVersionId,
      visualMode,
      AudioAssetMode.None,
      visual,
      AudioManifest: null,
      new AssetFallbackPolicy(AllowSilence: true, FallbackAssetRefs: []),
      moderation,
      createdAt,
      ComputeManifestHash(cardId, cardVersionId, visualMode.ToString(), asset.AssetRef, asset.ContentHash));
  }

  public AssetManifest BuildAudioManifest(
    string generationId,
    string cardId,
    string cardVersionId,
    AudioAssetMode audioMode,
    GeneratedAssetProviderResult asset,
    IReadOnlyList<AudioCueType> cueTypes,
    DateTimeOffset createdAt)
  {
    ArgumentNullException.ThrowIfNull(asset);
    if (audioMode is AudioAssetMode.None)
    {
      throw new ArgumentException("Audio manifest generation requires a non-NONE audio mode.", nameof(audioMode));
    }

    var moderation = ToModerationStatus(asset.ModerationStatus);
    var cues = (cueTypes.Count == 0 ? [AudioCueType.OnPlay] : cueTypes)
      .Distinct()
      .OrderBy(cueType => cueType.ToString(), StringComparer.Ordinal)
      .Select(cueType => new AudioCueDefinition(
        $"audio:{generationId}:{cueType}",
        cueType,
        $"{asset.AssetRef}#{cueType}",
        DurationMs: 1200,
        Language: null,
        SubtitleKey: null,
        VolumeGroup: "cards",
        new PlaybackPolicy(
          PlayOnce: true,
          InterruptPolicy: "DO_NOT_INTERRUPT",
          OverlapPolicy: "SKIP_IF_BUSY",
          Priority: 10,
          MaxConcurrentInstances: 1,
          ClientMaySkip: true),
        new CooldownPolicy(
          CooldownMs: 500,
          Scope: "CARD_INSTANCE"),
        FallbackCueId: null,
        asset.ContentHash))
      .ToArray();
    var audio = new AudioAssetManifest(
      $"audio:{generationId}:{audioMode}",
      "audio-bootstrap-0.1.0",
      cardId,
      cardVersionId,
      audioMode,
      cues,
      FallbackCueRefs: [],
      LocalizationPolicy: "none",
      VolumeGroup: "cards",
      moderation,
      ComputeManifestHash(cardId, cardVersionId, audioMode.ToString(), asset.AssetRef, asset.ContentHash, string.Join(",", cues.Select(cue => cue.CueType.ToString()))));

    return new AssetManifest(
      $"asset:{generationId}:{audioMode}",
      "asset-manifest-bootstrap-0.1.0",
      cardId,
      cardVersionId,
      VisualAssetMode.None,
      audioMode,
      VisualManifest: null,
      audio,
      new AssetFallbackPolicy(AllowSilence: true, FallbackAssetRefs: []),
      moderation,
      createdAt,
      ComputeManifestHash(cardId, cardVersionId, audioMode.ToString(), audio.AudioManifestId, audio.ContentHash));
  }

  private static AssetModerationStatus ToModerationStatus(string status) =>
    status switch
    {
      "PASSED" => AssetModerationStatus.Passed,
      "NOT_REQUIRED" => AssetModerationStatus.NotRequired,
      "PENDING" => AssetModerationStatus.Pending,
      "REJECTED" => AssetModerationStatus.Rejected,
      _ => AssetModerationStatus.Rejected
    };

  private static string ComputeManifestHash(params string[] parts)
  {
    var canonical = string.Join("|", parts);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
  }
}
