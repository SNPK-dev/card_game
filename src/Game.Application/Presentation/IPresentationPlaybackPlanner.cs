namespace Game.Application.Presentation;

public interface IPresentationPlaybackPlanner
{
  PresentationPlaybackPlanBuildResult Build(PresentationPlaybackPlanBuildRequest request);
}

