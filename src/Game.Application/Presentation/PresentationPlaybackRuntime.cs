using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed class PresentationPlaybackRuntime : IPresentationPlaybackRuntime
{
  public PresentationPlaybackRuntimeResult BuildInstructions(PresentationPlaybackRuntimeRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.PlaybackPlan);
    ArgumentNullException.ThrowIfNull(request.ClientState);

    var played = new HashSet<string>(request.ClientState.PlayedStepIds, StringComparer.Ordinal);
    var activeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var pair in request.ClientState.ActiveInstanceCounts)
    {
      activeCounts[pair.Key] = pair.Value;
    }
    var instructions = new List<PresentationPlaybackInstruction>();

    foreach (var step in request.PlaybackPlan.Steps
      .OrderBy(step => step.SourceEventSeq)
      .ThenBy(step => step.StepId, StringComparer.Ordinal))
    {
      var instruction = BuildInstruction(step, played, activeCounts);
      instructions.Add(instruction);

      if (instruction.InstructionKind is not PresentationPlaybackStepKind.PlayAsset
        || string.IsNullOrWhiteSpace(instruction.AssetRef))
      {
        continue;
      }

      played.Add(step.StepId);
      activeCounts[instruction.AssetRef] = activeCounts.TryGetValue(instruction.AssetRef, out var activeCount)
        ? activeCount + 1
        : 1;
    }

    return new PresentationPlaybackRuntimeResult(
      instructions,
      new PresentationPlaybackRuntimeState(
        played.Order(StringComparer.Ordinal).ToArray(),
        activeCounts));
  }

  private static PresentationPlaybackInstruction BuildInstruction(
    PresentationPlaybackStep step,
    ISet<string> played,
    IReadOnlyDictionary<string, int> activeCounts)
  {
    if (step.StepKind is PresentationPlaybackStepKind.Skip)
    {
      return CreateInstruction(step, PresentationPlaybackStepKind.Skip, null, step.Reason);
    }

    if (step.StepKind is PresentationPlaybackStepKind.Silence)
    {
      return step.FallbackPolicy.AllowSilence
        ? CreateInstruction(step, PresentationPlaybackStepKind.Silence, null, step.Reason)
        : CreateInstruction(step, PresentationPlaybackStepKind.Skip, null, "SILENCE_NOT_ALLOWED");
    }

    if (string.IsNullOrWhiteSpace(step.AssetRef))
    {
      return CreateInstruction(step, PresentationPlaybackStepKind.Skip, null, "ASSET_REF_MISSING");
    }

    if (step.PlaybackPolicy.PlayOnce && played.Contains(step.StepId))
    {
      return CreateInstruction(step, PresentationPlaybackStepKind.Skip, null, "PLAY_ONCE_ALREADY_CONSUMED");
    }

    var activeCount = activeCounts.TryGetValue(step.AssetRef, out var count) ? count : 0;
    if (step.PlaybackPolicy.MaxConcurrentInstances <= activeCount
      && string.Equals(step.PlaybackPolicy.OverlapPolicy, "SKIP_IF_BUSY", StringComparison.Ordinal))
    {
      return CreateInstruction(step, PresentationPlaybackStepKind.Skip, null, "ASSET_BUSY");
    }

    return CreateInstruction(step, PresentationPlaybackStepKind.PlayAsset, step.AssetRef, step.Reason);
  }

  private static PresentationPlaybackInstruction CreateInstruction(
    PresentationPlaybackStep step,
    PresentationPlaybackStepKind instructionKind,
    string? assetRef,
    string reason) =>
    new(
      step.StepId,
      instructionKind,
      step.TimelineEntryId,
      step.SourceEventSeq,
      step.CueType,
      assetRef,
      step.PlaybackPolicy.Priority,
      step.PlaybackPolicy.ClientMaySkip,
      reason);
}
