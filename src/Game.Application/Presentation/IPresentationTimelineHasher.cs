using Game.Shared.Assets;

namespace Game.Application.Presentation;

public interface IPresentationTimelineHasher
{
  string HashTimeline(PresentationTimeline timeline);
}
