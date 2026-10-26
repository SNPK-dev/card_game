using Game.Application.Presentation;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Assets;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Application.Tests;

public sealed class PresentationCueResolverTests
{
  [Fact]
  public void ResolveCreatesOnPlayCueForMatchingCardPlayedEvent()
  {
    var resolver = new PresentationCueResolver();
    var committedEvent = CreateEvent(
      7,
      CommittedEventType.CardPlayed,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1");
    var manifest = CreateManifest("asset-card-001-v1", "card_arcane_apprentice", "card_arcane_apprentice_v1");

    var result = resolver.Resolve(new PresentationCueResolutionRequest([committedEvent], [manifest]));

    var cue = Assert.Single(result.Cues);
    Assert.Equal("presentation:7:asset-card-001-v1:cue-on-play", cue.CueId);
    Assert.Equal(AudioCueType.OnPlay, cue.CueType);
    Assert.Equal(7, cue.SourceEventSeq);
    Assert.Equal("card_arcane_apprentice", cue.SourceCardId);
    Assert.Equal("card_arcane_apprentice_v1", cue.SourceCardVersionId);
    Assert.Equal("asset-card-001-v1", cue.AssetManifestId);
    Assert.Equal("asset://audio/cue-on-play.ogg", cue.AssetRef);
    Assert.Equal("Public", cue.VisibilityPolicy);
  }

  [Fact]
  public void ResolveReturnsEmptyWhenAssetOrCueIsMissing()
  {
    var resolver = new PresentationCueResolver();
    var committedEvent = CreateEvent(
      7,
      CommittedEventType.CardPlayed,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1");
    var summonOnlyManifest = CreateManifest(
      "asset-card-001-v1",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-on-summon", AudioCueType.OnSummon)]);

    var noManifestResult = resolver.Resolve(new PresentationCueResolutionRequest([committedEvent], []));
    var noCueResult = resolver.Resolve(new PresentationCueResolutionRequest([committedEvent], [summonOnlyManifest]));

    Assert.Empty(noManifestResult.Cues);
    Assert.Empty(noCueResult.Cues);
  }

  [Fact]
  public void ResolveOrdersCuesDeterministicallyAcrossManifestOrdering()
  {
    var resolver = new PresentationCueResolver();
    var committedEvent = CreateEvent(
      7,
      CommittedEventType.CardPlayed,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1");
    var firstManifest = CreateManifest(
      "asset-card-a",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-b", AudioCueType.OnPlay), CreateCue("cue-a", AudioCueType.OnPlay)]);
    var secondManifest = CreateManifest(
      "asset-card-b",
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      [CreateCue("cue-a", AudioCueType.OnPlay)]);

    var firstResult = resolver.Resolve(new PresentationCueResolutionRequest(
      [committedEvent],
      [secondManifest, firstManifest]));
    var secondResult = resolver.Resolve(new PresentationCueResolutionRequest(
      [committedEvent],
      [firstManifest, secondManifest]));

    Assert.Equal(
      ["presentation:7:asset-card-a:cue-a", "presentation:7:asset-card-a:cue-b", "presentation:7:asset-card-b:cue-a"],
      firstResult.Cues.Select(cue => cue.CueId));
    Assert.Equal(
      firstResult.Cues.Select(cue => cue.CueId),
      secondResult.Cues.Select(cue => cue.CueId));
  }

  [Fact]
  public void ResolvingPresentationCuesDoesNotChangeGameplayStateHash()
  {
    var state = new InMemoryGameState().WithCounter("life", 20);
    var hasher = new StateHasher(new CanonicalJsonSerializer());
    var before = hasher.HashGameplayState(state);
    var resolver = new PresentationCueResolver();

    var result = resolver.Resolve(new PresentationCueResolutionRequest(
      [CreateEvent(7, CommittedEventType.CardPlayed, "card_arcane_apprentice", "card_arcane_apprentice_v1")],
      [CreateManifest("asset-card-001-v1", "card_arcane_apprentice", "card_arcane_apprentice_v1")]));

    Assert.Single(result.Cues);
    Assert.Equal(before, hasher.HashGameplayState(state));
    Assert.Equal(1, state.Revision);
  }

  private static CommittedEventRecord CreateEvent(
    long eventSeq,
    CommittedEventType eventType,
    string cardId,
    string cardVersionId) =>
    new(
      eventSeq,
      null,
      1,
      eventType,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("CARD", cardId, "HAND", "player-1")],
      [],
      [
        new EventPayloadEntry("cardId", new EventPayloadValue("STRING", cardId, null, null, [])),
        new EventPayloadEntry("cardVersionId", new EventPayloadValue("STRING", cardVersionId, null, null, []))
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
    IReadOnlyList<AudioCueDefinition>? cues = null) =>
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
        cues ?? [CreateCue("cue-on-play", AudioCueType.OnPlay)],
        [],
        "none",
        "cards",
        AssetModerationStatus.Passed,
        $"sha256:audio-{assetManifestId}"),
      new AssetFallbackPolicy(true, []),
      AssetModerationStatus.Passed,
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      $"sha256:asset-{assetManifestId}");

  private static AudioCueDefinition CreateCue(
    string cueId,
    AudioCueType cueType) =>
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
      $"sha256:{cueId}");
}
