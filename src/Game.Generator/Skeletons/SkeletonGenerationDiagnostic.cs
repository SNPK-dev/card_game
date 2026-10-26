namespace Game.Generator.Skeletons;

public sealed record SkeletonGenerationDiagnostic(
  SkeletonGenerationDiagnosticCode Code,
  string Path,
  string ActualValue,
  string Message);
