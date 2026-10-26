using Game.Shared.Assets;

namespace Game.Application.Presentation;

public sealed record PresentationPlaybackPlanBuildRequest(
  string PlaybackPlanId,
  PresentationTimeline Timeline,
  string PresentationTimelineHash);

