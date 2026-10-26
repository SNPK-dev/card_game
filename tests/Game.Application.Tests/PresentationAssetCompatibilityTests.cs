using Game.Application.Capabilities;
using Game.Application.Presentation;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Assets;
using Xunit;

namespace Game.Application.Tests;

public sealed class PresentationAssetCompatibilityTests
{
  [Fact]
  public void ValidateAcceptsModeratedAssetManifestWithoutAudio()
  {
    var validator = new PresentationAssetCompatibilityValidator();

    var result = validator.Validate(
      CreateManifest(AudioAssetMode.None, audioManifest: null),
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry());

    Assert.True(result.IsCompatible);
    Assert.Empty(result.Diagnostics);
  }

  [Fact]
  public void ValidateRejectsUnsupportedVersionPendingModerationAndMissingSfxCue()
  {
    var manifest = CreateManifest(
      AudioAssetMode.SfxOnly,
      assetManifestVersion: "asset-manifest-future",
      moderationStatus: AssetModerationStatus.Pending,
      audioManifest: CreateAudioManifest(AudioAssetMode.SfxOnly, cues: []));
    var validator = new PresentationAssetCompatibilityValidator();

    var result = validator.Validate(
      manifest,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry());

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UNSUPPORTED_ASSET_MANIFEST_VERSION");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ASSET_MODERATION_NOT_PASSED");
    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SFX_CUE_REQUIRED");
  }

  [Fact]
  public void ValidateRejectsPendingVisualAndRejectedAudioModeration()
  {
    var manifest = CreateManifest(
      AudioAssetMode.SfxOnly,
      visualModerationStatus: AssetModerationStatus.Pending,
      audioManifest: CreateAudioManifest(
        AudioAssetMode.SfxOnly,
        moderationStatus: AssetModerationStatus.Rejected));
    var validator = new PresentationAssetCompatibilityValidator();

    var result = validator.Validate(
      manifest,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry());

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "VISUAL_ASSET_MODERATION_NOT_PASSED"
      && diagnostic.Path == "assetManifest.visualManifest.moderationStatus");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "AUDIO_ASSET_MODERATION_NOT_PASSED"
      && diagnostic.Path == "assetManifest.audioManifest.moderationStatus");
  }

  [Fact]
  public void ValidateRejectsVoiceLineOnlyWithoutVoiceCueOrFallback()
  {
    var manifest = CreateManifest(
      AudioAssetMode.VoiceLineOnly,
      allowSilenceFallback: false,
      audioManifest: CreateAudioManifest(
        AudioAssetMode.VoiceLineOnly,
        cues: [CreateCue("cue-on-play", cueType: AudioCueType.OnPlay)]));
    var validator = new PresentationAssetCompatibilityValidator();

    var result = validator.Validate(
      manifest,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry());

    Assert.False(result.IsCompatible);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == "VOICE_CUE_OR_FALLBACK_REQUIRED"
      && diagnostic.Path == "assetManifest.audioManifest.cues");
  }

  [Fact]
  public void ValidateAcceptsHybridAudioModeWithSfxAndVoiceCues()
  {
    var manifest = CreateManifest(
      AudioAssetMode.SfxAndVoice,
      allowSilenceFallback: false,
      audioManifest: CreateAudioManifest(
        AudioAssetMode.SfxAndVoice,
        cues:
        [
          CreateCue("cue-on-play", cueType: AudioCueType.OnPlay),
          CreateCue("cue-on-inspect", cueType: AudioCueType.OnInspect)
        ]));
    var validator = new PresentationAssetCompatibilityValidator();

    var result = validator.Validate(
      manifest,
      new DefaultEngineCapabilityRegistryProvider().GetCurrentRegistry());

    Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Path}")));
  }

  [Fact]
  public void PresentationHashIsStableAcrossCueOrdering()
  {
    var hasher = new PresentationCatalogHasher();
    var first = CreateManifest(
      AudioAssetMode.SfxOnly,
      audioManifest: CreateAudioManifest(AudioAssetMode.SfxOnly, cues: [CreateCue("cue-b"), CreateCue("cue-a")]));
    var second = CreateManifest(
      AudioAssetMode.SfxOnly,
      audioManifest: CreateAudioManifest(AudioAssetMode.SfxOnly, cues: [CreateCue("cue-a"), CreateCue("cue-b")]));

    Assert.Equal(hasher.HashManifest(first), hasher.HashManifest(second));
  }

  [Fact]
  public void PresentationHashChangesWhenAudioContentHashChanges()
  {
    var hasher = new PresentationCatalogHasher();
    var first = CreateManifest(AudioAssetMode.SfxOnly);
    var second = CreateManifest(
      AudioAssetMode.SfxOnly,
      audioManifest: CreateAudioManifest(AudioAssetMode.SfxOnly, cues: [CreateCue("cue-on-play", "sha256:changed")]));

    Assert.NotEqual(hasher.HashManifest(first), hasher.HashManifest(second));
  }

  [Fact]
  public void PresentationHashDoesNotReplaceGameplayStateHash()
  {
    var state = new InMemoryGameState().WithCounter("life", 20);
    var decoratedState = state.WithPresentationMetadata("assetManifestId", "asset-card-001-v1");
    var gameplayHasher = new StateHasher(new CanonicalJsonSerializer());
    var presentationHash = new PresentationCatalogHasher().HashManifest(CreateManifest(AudioAssetMode.SfxOnly));

    Assert.Equal(gameplayHasher.HashGameplayState(state), gameplayHasher.HashGameplayState(decoratedState));
    Assert.NotEqual(gameplayHasher.HashGameplayState(state), presentationHash);
  }

  private static AssetManifest CreateManifest(
    AudioAssetMode audioMode,
    string assetManifestVersion = "asset-manifest-bootstrap-0.1.0",
    AssetModerationStatus moderationStatus = AssetModerationStatus.Passed,
    AssetModerationStatus visualModerationStatus = AssetModerationStatus.Passed,
    bool allowSilenceFallback = true,
    AudioAssetManifest? audioManifest = null) =>
    new(
      "asset-card-001-v1",
      assetManifestVersion,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      VisualAssetMode.StaticImage,
      audioMode,
      new VisualAssetManifest(
        "visual-card-001-v1",
        "visual-bootstrap-0.1.0",
        "asset://visual/card_arcane_apprentice.png",
        "png",
        visualModerationStatus,
        "sha256:visual"),
      audioMode is AudioAssetMode.None
        ? null
        : audioManifest ?? CreateAudioManifest(audioMode),
      new AssetFallbackPolicy(allowSilenceFallback, []),
      moderationStatus,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      "sha256:asset-root");

  private static AudioAssetManifest CreateAudioManifest(
    AudioAssetMode audioMode,
    IReadOnlyList<AudioCueDefinition>? cues = null,
    AssetModerationStatus moderationStatus = AssetModerationStatus.Passed) =>
    new(
      "audio-card-001-v1",
      "audio-bootstrap-0.1.0",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      audioMode,
      cues ?? [CreateCue("cue-on-play")],
      [],
      "none",
      "cards",
      moderationStatus,
      "sha256:audio");

  private static AudioCueDefinition CreateCue(
    string cueId,
    string contentHash = "sha256:audio-on-play",
    AudioCueType cueType = AudioCueType.OnPlay) =>
    new(
      cueId,
      cueType,
      $"asset://audio/{cueId}.ogg",
      1200,
      null,
      null,
      "cards",
      new PlaybackPolicy(true, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
      new CooldownPolicy(500, "CARD_INSTANCE"),
      null,
      contentHash);
}
