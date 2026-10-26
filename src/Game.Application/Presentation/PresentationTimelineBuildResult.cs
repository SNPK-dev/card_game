using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed record PresentationTimelineBuildResult(
  PresentationTimeline Timeline,
  string PresentationTimelineHash);
