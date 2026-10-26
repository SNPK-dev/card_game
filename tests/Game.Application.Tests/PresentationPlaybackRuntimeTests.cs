using Game.Application.Presentation;
using Game.Shared.Assets;
using Xunit;

namespace Game.Application.Tests;

public sealed class PresentationPlaybackRuntimeTests
{
  [Fact]
  public void BuildInstructionsProducesClientPlayableSilenceAndSkipInstructions()
  {
    var plan = CreatePlan([
      CreateStep("step-play", PresentationPlaybackStepKind.PlayAsset, 1, "asset://audio/play.ogg", PlayOnce: true),
      CreateStep("step-silence", PresentationPlaybackStepKind.Silence, 2, null, PlayOnce: false),
      CreateStep("step-skip", PresentationPlaybackStepKind.Skip, 3, null, PlayOnce: false)
    ]);

    var result = new PresentationPlaybackRuntime().BuildInstructions(new PresentationPlaybackRuntimeRequest(
      plan,
      new PresentationPlaybackRuntimeState(
        [],
        new SortedDictionary<string, int>(StringComparer.Ordinal)),
      NowMs: 100));

    Assert.Equal(
      [PresentationPlaybackStepKind.PlayAsset, PresentationPlaybackStepKind.Silence, PresentationPlaybackStepKind.Skip],
      result.Instructions.Select(instruction => instruction.InstructionKind));
    Assert.Equal("asset://audio/play.ogg", result.Instructions[0].AssetRef);
    Assert.Null(result.Instructions[1].AssetRef);
    Assert.Equal("NO_PLAYABLE_PRESENTATION_OUTPUT", result.Instructions[2].Reason);
    Assert.Contains("step-play", result.NextState.PlayedStepIds);
    Assert.Equal(1, result.NextState.ActiveInstanceCounts["asset://audio/play.ogg"]);
  }

  [Fact]
  public void BuildInstructionsHonorsPlayOnceAndBusyOverlapPolicy()
  {
    var plan = CreatePlan([
      CreateStep("step-play-once", PresentationPlaybackStepKind.PlayAsset, 1, "asset://audio/once.ogg", PlayOnce: true),
      CreateStep("step-busy", PresentationPlaybackStepKind.PlayAsset, 2, "asset://audio/busy.ogg", PlayOnce: false)
    ]);
    var state = new PresentationPlaybackRuntimeState(
      ["step-play-once"],
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["asset://audio/busy.ogg"] = 1
      });

    var result = new PresentationPlaybackRuntime().BuildInstructions(new PresentationPlaybackRuntimeRequest(
      plan,
      state,
      NowMs: 200));

    Assert.Equal([PresentationPlaybackStepKind.Skip, PresentationPlaybackStepKind.Skip], result.Instructions.Select(i => i.InstructionKind));
    Assert.Equal("PLAY_ONCE_ALREADY_CONSUMED", result.Instructions[0].Reason);
    Assert.Equal("ASSET_BUSY", result.Instructions[1].Reason);
    Assert.Equal(1, result.NextState.ActiveInstanceCounts["asset://audio/busy.ogg"]);
  }

  private static PresentationPlaybackPlan CreatePlan(IReadOnlyList<PresentationPlaybackStep> steps) =>
    new(
      "playback-plan-1",
      "timeline-1",
      "sha256:timeline",
      "sha256:presentation-catalog",
      steps.Select(step => step.SourceEventSeq).Min(),
      steps.Select(step => step.SourceEventSeq).Max(),
      steps);

  private static PresentationPlaybackStep CreateStep(
    string stepId,
    PresentationPlaybackStepKind stepKind,
    long sourceEventSeq,
    string? assetRef,
    bool PlayOnce) =>
    new(
      stepId,
      stepKind,
      $"entry-{stepId}",
      sourceEventSeq,
      AudioCueType.OnPlay,
      "card_arcane_apprentice",
      "card_arcane_apprentice_v1",
      "asset-card-001-v1",
      assetRef,
      new PlaybackPolicy(PlayOnce, "DO_NOT_INTERRUPT", "SKIP_IF_BUSY", 10, 1, true),
      new AssetFallbackPolicy(true, []),
      "Public",
      stepKind is PresentationPlaybackStepKind.Skip ? "NO_PLAYABLE_PRESENTATION_OUTPUT" : "RESOLVED_CUE");
}
