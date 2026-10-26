using Game.Shared.Assets;
using Game.Shared.Engine;

namespace Game.Application.Presentation;

public sealed class PresentationAssetCompatibilityValidator : IPresentationAssetCompatibilityValidator
{
  private static readonly AudioCueType[] SfxCueTypes =
  [
    AudioCueType.OnPlay,
    AudioCueType.OnSummon,
    AudioCueType.OnAttack,
    AudioCueType.OnDeath,
    AudioCueType.OnTrigger,
    AudioCueType.OnSecretReveal,
    AudioCueType.OnQuestComplete
  ];

  private static readonly AudioCueType[] VoiceCueTypes =
  [
    AudioCueType.OnHover,
    AudioCueType.OnInspect,
    AudioCueType.OnCrafted
  ];

  public PresentationAssetCompatibilityResult Validate(
    AssetManifest manifest,
    EngineCapabilityRegistry registry)
  {
    ArgumentNullException.ThrowIfNull(manifest);
    ArgumentNullException.ThrowIfNull(registry);

    var diagnostics = new List<PresentationAssetCompatibilityDiagnostic>();

    AddRequired(diagnostics, manifest.AssetManifestId, "assetManifest.assetManifestId", "ASSET_MANIFEST_ID_REQUIRED");
    AddRequired(diagnostics, manifest.AssetManifestVersion, "assetManifest.assetManifestVersion", "ASSET_MANIFEST_VERSION_REQUIRED");
    AddRequired(diagnostics, manifest.CardId, "assetManifest.cardId", "ASSET_CARD_ID_REQUIRED");
    AddRequired(diagnostics, manifest.CardVersionId, "assetManifest.cardVersionId", "ASSET_CARD_VERSION_REQUIRED");
    AddRequired(diagnostics, manifest.ContentHash, "assetManifest.contentHash", "ASSET_CONTENT_HASH_REQUIRED");

    if (!registry.SupportedAssetManifestVersions.Contains(manifest.AssetManifestVersion, StringComparer.Ordinal))
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "UNSUPPORTED_ASSET_MANIFEST_VERSION",
        "assetManifest.assetManifestVersion",
        manifest.AssetManifestVersion,
        $"Asset manifest version '{manifest.AssetManifestVersion}' is not supported by engine '{registry.EngineVersion}'."));
    }

    if (manifest.ModerationStatus is AssetModerationStatus.Pending or AssetModerationStatus.Rejected)
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "ASSET_MODERATION_NOT_PASSED",
        "assetManifest.moderationStatus",
        manifest.ModerationStatus.ToString(),
        "Asset manifest moderation must be passed or not required before publication compatibility."));
    }

    ValidateVisualManifest(manifest, diagnostics);
    ValidateAudioManifest(manifest, diagnostics);

    return diagnostics.Count == 0
      ? PresentationAssetCompatibilityResult.Compatible()
      : PresentationAssetCompatibilityResult.Rejected(diagnostics);
  }

  private static void ValidateVisualManifest(
    AssetManifest manifest,
    ICollection<PresentationAssetCompatibilityDiagnostic> diagnostics)
  {
    if (manifest.VisualAssetMode is VisualAssetMode.None)
    {
      return;
    }

    if (manifest.VisualManifest is null)
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "VISUAL_MANIFEST_REQUIRED",
        "assetManifest.visualManifest",
        manifest.AssetManifestId,
        $"Visual manifest is required when visual mode is '{manifest.VisualAssetMode}'."));
      return;
    }

    AddRequired(diagnostics, manifest.VisualManifest.AssetRef, "assetManifest.visualManifest.assetRef", "VISUAL_ASSET_REF_REQUIRED");
    AddRequired(diagnostics, manifest.VisualManifest.ContentHash, "assetManifest.visualManifest.contentHash", "VISUAL_CONTENT_HASH_REQUIRED");
    AddModerationDiagnostics(
      diagnostics,
      manifest.VisualManifest.ModerationStatus,
      "assetManifest.visualManifest.moderationStatus",
      "VISUAL_ASSET_MODERATION_NOT_PASSED",
      "Visual asset moderation must be passed or not required before publication compatibility.");
  }

  private static void ValidateAudioManifest(
    AssetManifest manifest,
    ICollection<PresentationAssetCompatibilityDiagnostic> diagnostics)
  {
    if (manifest.AudioAssetMode is AudioAssetMode.None)
    {
      return;
    }

    if (manifest.AudioManifest is null)
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "AUDIO_MANIFEST_REQUIRED",
        "assetManifest.audioManifest",
        manifest.AssetManifestId,
        $"Audio manifest is required when audio mode is '{manifest.AudioAssetMode}'."));
      return;
    }

    AddRequired(diagnostics, manifest.AudioManifest.AudioManifestId, "assetManifest.audioManifest.audioManifestId", "AUDIO_MANIFEST_ID_REQUIRED");
    AddRequired(diagnostics, manifest.AudioManifest.ContentHash, "assetManifest.audioManifest.contentHash", "AUDIO_CONTENT_HASH_REQUIRED");
    AddModerationDiagnostics(
      diagnostics,
      manifest.AudioManifest.ModerationStatus,
      "assetManifest.audioManifest.moderationStatus",
      "AUDIO_ASSET_MODERATION_NOT_PASSED",
      "Audio asset moderation must be passed or not required before publication compatibility.");

    if (manifest.AudioManifest.AudioAssetMode != manifest.AudioAssetMode)
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "AUDIO_MODE_MISMATCH",
        "assetManifest.audioManifest.audioAssetMode",
        manifest.AudioManifest.AudioAssetMode.ToString(),
        "Audio manifest mode must match the parent asset manifest mode."));
    }

    if (manifest.AudioAssetMode is AudioAssetMode.SfxOnly && manifest.AudioManifest.Cues.Count == 0)
    {
      diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
        "SFX_CUE_REQUIRED",
        "assetManifest.audioManifest.cues",
        manifest.AssetManifestId,
        "SFX_ONLY asset manifests require at least one audio cue."));
    }

    ValidateAudioModeCueCoverage(manifest, diagnostics);

    foreach (var cue in manifest.AudioManifest.Cues)
    {
      AddRequired(diagnostics, cue.CueId, "assetManifest.audioManifest.cues[].cueId", "AUDIO_CUE_ID_REQUIRED");
      AddRequired(diagnostics, cue.AssetRef, "assetManifest.audioManifest.cues[].assetRef", "AUDIO_CUE_ASSET_REF_REQUIRED");
      AddRequired(diagnostics, cue.ContentHash, "assetManifest.audioManifest.cues[].contentHash", "AUDIO_CUE_CONTENT_HASH_REQUIRED");
    }
  }

  private static void ValidateAudioModeCueCoverage(
    AssetManifest manifest,
    ICollection<PresentationAssetCompatibilityDiagnostic> diagnostics)
  {
    if (manifest.AudioManifest is null)
    {
      return;
    }

    var cueTypes = manifest.AudioManifest.Cues
      .Select(cue => cue.CueType)
      .ToHashSet();
    var hasFallback = manifest.AudioManifest.FallbackCueRefs.Count > 0 || manifest.FallbackPolicy.AllowSilence;

    switch (manifest.AudioAssetMode)
    {
      case AudioAssetMode.VoiceLineOnly:
        if (!cueTypes.Overlaps(VoiceCueTypes) && !hasFallback)
        {
          diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
            "VOICE_CUE_OR_FALLBACK_REQUIRED",
            "assetManifest.audioManifest.cues",
            manifest.AssetManifestId,
            "VOICE_LINE_ONLY asset manifests require at least one voice cue or an explicit fallback policy."));
        }

        break;
      case AudioAssetMode.SfxAndVoice:
        if (!cueTypes.Overlaps(SfxCueTypes))
        {
          diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
            "SFX_CUE_REQUIRED",
            "assetManifest.audioManifest.cues",
            manifest.AssetManifestId,
            "SFX_AND_VOICE asset manifests require at least one SFX cue."));
        }

        if (!cueTypes.Overlaps(VoiceCueTypes) && !hasFallback)
        {
          diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
            "VOICE_CUE_OR_FALLBACK_REQUIRED",
            "assetManifest.audioManifest.cues",
            manifest.AssetManifestId,
            "SFX_AND_VOICE asset manifests require at least one voice cue or an explicit fallback policy."));
        }

        break;
      case AudioAssetMode.FullAudioSet:
        var missing = Enum.GetValues<AudioCueType>()
          .Where(cueType => !cueTypes.Contains(cueType))
          .OrderBy(cueType => cueType.ToString(), StringComparer.Ordinal)
          .ToArray();
        if (missing.Length > 0 && !hasFallback)
        {
          diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
            "FULL_AUDIO_SET_CUES_OR_FALLBACK_REQUIRED",
            "assetManifest.audioManifest.cues",
            string.Join(",", missing.Select(cueType => cueType.ToString())),
            "FULL_AUDIO_SET asset manifests require every known audio cue type or an explicit fallback policy."));
        }

        break;
    }
  }

  private static void AddRequired(
    ICollection<PresentationAssetCompatibilityDiagnostic> diagnostics,
    string? value,
    string path,
    string code)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
      code,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }

  private static void AddModerationDiagnostics(
    ICollection<PresentationAssetCompatibilityDiagnostic> diagnostics,
    AssetModerationStatus status,
    string path,
    string code,
    string message)
  {
    if (status is not (AssetModerationStatus.Pending or AssetModerationStatus.Rejected))
    {
      return;
    }

    diagnostics.Add(new PresentationAssetCompatibilityDiagnostic(
      code,
      path,
      status.ToString(),
      message));
  }
}
