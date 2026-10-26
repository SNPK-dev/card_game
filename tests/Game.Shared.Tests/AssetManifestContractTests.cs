using System.Reflection;
using Game.Shared.Assets;
using Xunit;

namespace Game.Shared.Tests;

public sealed class AssetManifestContractTests
{
  [Fact]
  public void AssetManifestCarriesPresentationDataOnly()
  {
    var manifest = CreateManifest(AudioAssetMode.SfxOnly);

    Assert.Equal("asset-card-001-v1", manifest.AssetManifestId);
    Assert.Equal("asset-manifest-bootstrap-0.1.0", manifest.AssetManifestVersion);
    Assert.Equal("card_arcane_apprentice", manifest.CardId);
    Assert.Equal(AudioAssetMode.SfxOnly, manifest.AudioAssetMode);
    Assert.NotNull(manifest.AudioManifest);
    Assert.Equal(AudioCueType.OnPlay, manifest.AudioManifest.Cues.Single().CueType);
    Assert.Equal("sha256:asset-root", manifest.ContentHash);
  }

  [Fact]
  public void PresentationTimelineCarriesDerivedDataOnly()
  {
    var timeline = new PresentationTimeline(
      "timeline-match-001",
      "sha256:presentation-catalog",
      7,
      9,
      [
        new PresentationTimelineEntry(
          "presentation:7:asset-card-001-v1:cue-on-play",
          PresentationTimelineEntryKind.Cue,
          7,
          AudioCueType.OnPlay,
          "card_arcane_apprentice",
          "card_arcane_apprentice_v1",
          "asset-card-001-v1",
          "asset://audio/cue-on-play.ogg",
          new PlaybackPolicy(true, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
          "Public",
          new AssetFallbackPolicy(true, []),
          "RESOLVED_CUE")
      ]);

    var entry = Assert.Single(timeline.Entries);
    Assert.Equal("timeline-match-001", timeline.TimelineId);
    Assert.Equal(PresentationTimelineEntryKind.Cue, entry.EntryKind);
    Assert.Equal(7, entry.SourceEventSeq);
    Assert.Equal("asset-card-001-v1", entry.AssetManifestId);
  }

  [Fact]
  public void AssetContractsRemainRecordsOrEnums()
  {
    var contractTypes = typeof(AssetManifest).Assembly
      .GetExportedTypes()
      .Where(type => type.Namespace is "Game.Shared.Assets")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.True(type.IsEnum || IsRecord(type));
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Hasher", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Executor", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("CalculateHash", declaredMethods);
      Assert.DoesNotContain("HashManifest", declaredMethods);
      Assert.DoesNotContain("Play", declaredMethods);
      Assert.DoesNotContain("Generate", declaredMethods);
    });
  }

  private static AssetManifest CreateManifest(AudioAssetMode audioMode) =>
    new(
      "asset-card-001-v1",
      "asset-manifest-bootstrap-0.1.0",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      VisualAssetMode.StaticImage,
      audioMode,
      new VisualAssetManifest(
        "visual-card-001-v1",
        "visual-bootstrap-0.1.0",
        "asset://visual/card_arcane_apprentice.png",
        "png",
        AssetModerationStatus.Passed,
        "sha256:visual"),
      new AudioAssetManifest(
        "audio-card-001-v1",
        "audio-bootstrap-0.1.0",
        "card_arcane_apprentice",
        "card_arcane_apprentice_v1",
        audioMode,
        [
          new AudioCueDefinition(
            "cue-on-play",
            AudioCueType.OnPlay,
            "asset://audio/card_arcane_apprentice_on_play.ogg",
            1200,
            null,
            null,
            "cards",
            new PlaybackPolicy(true, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
            new CooldownPolicy(500, "CARD_INSTANCE"),
            null,
            "sha256:audio-on-play")
        ],
        [],
        "none",
        "cards",
        AssetModerationStatus.Passed,
        "sha256:audio"),
      new AssetFallbackPolicy(true, []),
      AssetModerationStatus.Passed,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      "sha256:asset-root");

  private static bool IsRecord(Type type)
  {
    return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Any(method => method.Name == "<Clone>$");
  }
}
