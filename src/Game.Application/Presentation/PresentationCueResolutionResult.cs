using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed record PresentationCueResolutionResult(
  IReadOnlyList<PresentationCue> Cues);
