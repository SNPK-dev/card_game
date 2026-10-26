using Game.Shared.Assets;

namespace Game.Application.Presentation;

public interface IPresentationPlaybackRuntime
{
  PresentationPlaybackRuntimeResult BuildInstructions(PresentationPlaybackRuntimeRequest request);
}

public sealed record PresentationPlaybackRuntimeRequest(
  PresentationPlaybackPlan PlaybackPlan,
  PresentationPlaybackRuntimeState ClientState,
  int NowMs);

public sealed record PresentationPlaybackRuntimeState(
  IReadOnlyList<string> PlayedStepIds,
  IReadOnlyDictionary<string, int> ActiveInstanceCounts);

public sealed record PresentationPlaybackRuntimeResult(
  IReadOnlyList<PresentationPlaybackInstruction> Instructions,
  PresentationPlaybackRuntimeState NextState);

public sealed record PresentationPlaybackInstruction(
  string StepId,
  PresentationPlaybackStepKind InstructionKind,
  string TimelineEntryId,
  long SourceEventSeq,
  AudioCueType CueType,
  string? AssetRef,
  int Priority,
  bool ClientMaySkip,
  string Reason);
