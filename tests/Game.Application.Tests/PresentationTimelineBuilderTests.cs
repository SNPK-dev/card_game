using Game.Application.Presentation;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Assets;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class PresentationTimelineBuilderTests
{
  [Fact]
  public void BuildOrdersCueEntriesDeterministically()
  {
    var builder = CreateBuilder();
    var firstManifest = CreateManifest(
      "asset-card-b",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-b", AudioCueType.OnPlay), CreateCue("cue-a", AudioCueType.OnPlay)]);
    var secondManifest = CreateManifest(
      "asset-card-a",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-a", AudioCueType.OnPlay)]);
    CommittedEventRecord[] events =
      [CreateEvent(9, CommittedEventType.CardPlayed), CreateEvent(7, CommittedEventType.CardPlayed)];

    var first = builder.Build(new PresentationTimelineBuildRequest(
      "timeline-1",
      events,
      [firstManifest, secondManifest]));
    var second = builder.Build(new PresentationTimelineBuildRequest(
      "timeline-1",
      events.Reverse().ToArray(),
      [secondManifest, firstManifest]));

    Assert.Equal(first.PresentationTimelineHash, second.PresentationTimelineHash);
    Assert.Equal(
      [
        "presentation:7:asset-card-a:cue-a",
        "presentation:7:asset-card-b:cue-a",
        "presentation:7:asset-card-b:cue-b",
        "presentation:9:asset-card-a:cue-a",
        "presentation:9:asset-card-b:cue-a",
        "presentation:9:asset-card-b:cue-b"
      ],
      first.Timeline.Entries.Select(entry => entry.EntryId));
  }

  [Fact]
  public void BuildCreatesSilenceEntryWhenMatchingCueIsMissing()
  {
    var builder = CreateBuilder();
    var manifest = CreateManifest(
      "asset-card-001-v1",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-on-summon", AudioCueType.OnSummon)]);

    var result = builder.Build(new PresentationTimelineBuildRequest(
      "timeline-1",
      [CreateEvent(7, CommittedEventType.CardPlayed)],
      [manifest]));

    var entry = Assert.Single(result.Timeline.Entries);
    Assert.Equal(PresentationTimelineEntryKind.Silence, entry.EntryKind);
    Assert.Equal("presentation:7:asset-card-001-v1:silence:OnPlay", entry.EntryId);
    Assert.Equal("MISSING_CUE_FALLBACK_SILENCE", entry.Reason);
    Assert.Null(entry.AssetRef);
  }

  [Fact]
  public void TimelineHashChangesWhenAudioContentHashChangesWithoutChangingGameplayStateHash()
  {
    var state = new InMemoryGameState().WithCounter("life", 20);
    var gameplayHasher = new StateHasher(new CanonicalJsonSerializer());
    var gameplayHash = gameplayHasher.HashGameplayState(state);
    var builder = CreateBuilder();
    var firstManifest = CreateManifest(
      "asset-card-001-v1",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-on-play", AudioCueType.OnPlay, "sha256:audio-a")]);
    var secondManifest = CreateManifest(
      "asset-card-001-v1",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-on-play", AudioCueType.OnPlay, "sha256:audio-b")]);
    var committedEvent = CreateEvent(7, CommittedEventType.CardPlayed);

    var first = builder.Build(new PresentationTimelineBuildRequest("timeline-1", [committedEvent], [firstManifest]));
    var second = builder.Build(new PresentationTimelineBuildRequest("timeline-1", [committedEvent], [secondManifest]));

    Assert.NotEqual(first.PresentationTimelineHash, second.PresentationTimelineHash);
    Assert.Equal(gameplayHash, gameplayHasher.HashGameplayState(state));
  }

  private static PresentationTimelineBuilder CreateBuilder()
  {
    var cueResolver = new PresentationCueResolver();
    var catalogHasher = new PresentationCatalogHasher();
    return new PresentationTimelineBuilder(cueResolver, catalogHasher, new PresentationTimelineHasher());
  }

  private static CommittedEventRecord CreateEvent(
    long eventSeq,
    CommittedEventType eventType) =>
    new(
      eventSeq,
      null,
      1,
      eventType,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("CARD", "card_arcane_apprentice", "HAND", "player-1")],
      [],
      [
        new EventPayloadEntry("cardId", new EventPayloadValue("STRING", "card_arcane_apprentice", null, null, [])),
        new EventPayloadEntry("cardVersionId", new EventPayloadValue("STRING", "card_arcane_apprentice_v1", null, null, []))
      ],
      "batch-1",
      ["mutation-1"],
      [],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);

  private static AssetManifest CreateManifest(
    string assetManifestId,
    string cardId,
    string cardVersionId,
    IReadOnlyList<AudioCueDefinition> cues) =>
    new(
      assetManifestId,
      "asset-manifest-bootstrap-0.1.0",
      cardId,
      cardVersionId,
      VisualAssetMode.StaticImage,
      AudioAssetMode.SfxOnly,
      new VisualAssetManifest(
        $"visual-{assetManifestId}",
        "visual-bootstrap-0.1.0",
        $"asset://visual/{assetManifestId}.png",
        "png",
        AssetModerationStatus.Passed,
        $"sha256:visual-{assetManifestId}"),
      new AudioAssetManifest(
        $"audio-{assetManifestId}",
        "audio-bootstrap-0.1.0",
        cardId,
        cardVersionId,
        AudioAssetMode.SfxOnly,
        cues,
        [],
        "none",
        "cards",
        AssetModerationStatus.Passed,
        $"sha256:audio-{assetManifestId}"),
      new AssetFallbackPolicy(true, []),
      AssetModerationStatus.Passed,
      DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
      $"sha256:asset-{assetManifestId}");

  private static AudioCueDefinition CreateCue(
    string cueId,
    AudioCueType cueType,
    string contentHash = "sha256:audio") =>
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
