namespace Game.Application.Presentation;

public interface IPresentationTimelineBuilder
{
  PresentationTimelineBuildResult Build(PresentationTimelineBuildRequest request);
}
