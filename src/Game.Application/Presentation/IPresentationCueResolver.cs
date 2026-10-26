namespace Game.Application.Presentation;

public interface IPresentationCueResolver
{
  PresentationCueResolutionResult Resolve(PresentationCueResolutionRequest request);
}
