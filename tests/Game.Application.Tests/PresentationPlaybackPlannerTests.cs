using Game.Application.Presentation;
using Game.Domain.Hashing;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Assets;
using Xunit;

namespace Game.Application.Tests;

public sealed class PresentationPlaybackPlannerTests
{
  [Fact]
  public void BuildCreatesPlayableSilenceAndSkipStepsFromTimeline()
  {
    var planner = new PresentationPlaybackPlanner();
    var timeline = CreateTimeline([
      CreateEntry("entry-private", PresentationTimelineEntryKind.Cue, 3, "asset://audio/private.ogg", "Private"),
      CreateEntry("entry-silence", PresentationTimelineEntryKind.Silence, 2, null, "Public"),
      CreateEntry("entry-play", PresentationTimelineEntryKind.Cue, 1, "asset://audio/play.ogg", "Public")
    ]);

    var result = planner.Build(new PresentationPlaybackPlanBuildRequest(
      "playback-plan-1",
      timeline,
      "sha256:timeline"));

    Assert.Equal("playback-plan-1", result.PlaybackPlan.PlaybackPlanId);
    Assert.Equal("timeline-1", result.PlaybackPlan.TimelineId);
    Assert.Equal("sha256:timeline", result.PlaybackPlan.PresentationTimelineHash);
    Assert.Equal(
      ["playback:entry-play", "playback:entry-silence", "playback:entry-private"],
      result.PlaybackPlan.Steps.Select(step => step.StepId));

    Assert.Equal(PresentationPlaybackStepKind.PlayAsset, result.PlaybackPlan.Steps[0].StepKind);
    Assert.Equal("asset://audio/play.ogg", result.PlaybackPlan.Steps[0].AssetRef);
    Assert.Equal(PresentationPlaybackStepKind.Silence, result.PlaybackPlan.Steps[1].StepKind);
    Assert.Null(result.PlaybackPlan.Steps[1].AssetRef);
    Assert.Equal(PresentationPlaybackStepKind.Skip, result.PlaybackPlan.Steps[2].StepKind);
    Assert.Equal("HIDDEN_BY_VISIBILITY_POLICY", result.PlaybackPlan.Steps[2].Reason);
  }

  [Fact]
  public void BuildSkipsCueEntriesWithoutPlayableAssetRef()
  {
    var planner = new PresentationPlaybackPlanner();
    var timeline = CreateTimeline([
      CreateEntry("entry-missing-asset", PresentationTimelineEntryKind.Cue, 1, null, "Public")
    ]);

    var result = planner.Build(new PresentationPlaybackPlanBuildRequest(
      "playback-plan-1",
      timeline,
      "sha256:timeline"));

    var step = Assert.Single(result.PlaybackPlan.Steps);
    Assert.Equal(PresentationPlaybackStepKind.Skip, step.StepKind);
    Assert.Equal("NO_PLAYABLE_PRESENTATION_OUTPUT", step.Reason);
    Assert.Null(step.AssetRef);
  }

  [Fact]
  public void BuildPlaybackPlanDoesNotAffectGameplayStateHash()
  {
    var state = new InMemoryGameState().WithCounter("life", 20);
    var gameplayHasher = new StateHasher(new CanonicalJsonSerializer());
    var before = gameplayHasher.HashGameplayState(state);
    var timeline = CreateTimeline([
      CreateEntry("entry-play", PresentationTimelineEntryKind.Cue, 1, "asset://audio/play.ogg", "Public")
    ]);

    _ = new PresentationPlaybackPlanner().Build(new PresentationPlaybackPlanBuildRequest(
      "playback-plan-1",
      timeline,
      "sha256:timeline"));

    Assert.Equal(before, gameplayHasher.HashGameplayState(state));
  }

  private static PresentationTimeline CreateTimeline(IReadOnlyList<PresentationTimelineEntry> entries) =>
    new(
      "timeline-1",
      "sha256:presentation-catalog",
      entries.Select(entry => entry.SourceEventSeq).Order().FirstOrDefault(),
      entries.Select(entry => entry.SourceEventSeq).Order().LastOrDefault(),
      entries);

  private static PresentationTimelineEntry CreateEntry(
    string entryId,
    PresentationTimelineEntryKind entryKind,
    long sourceEventSeq,
    string? assetRef,
    string visibilityPolicy) =>
    new(
      entryId,
      entryKind,
      sourceEventSeq,
      AudioCueType.OnPlay,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "asset-card-001-v1",
      assetRef,
      new PlaybackPolicy(true, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
      visibilityPolicy,
      new AssetFallbackPolicy(true, []),
      entryKind is PresentationTimelineEntryKind.Silence ? "MISSING_CUE_FALLBACK_SILENCE" : "RESOLVED_CUE");
}

