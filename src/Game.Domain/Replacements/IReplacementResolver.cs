namespace Game.Domain.Replacements;

public interface IReplacementResolver
{
  ReplacementResolutionResult Resolve(ReplacementResolutionRequest request);
}
