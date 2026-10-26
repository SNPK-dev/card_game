namespace Game.Generator.Skeletons;

public sealed record SkeletonGenerationResult(
  bool Succeeded,
  GeneratedCardSkeleton? Skeleton,
  IReadOnlyList<SkeletonGenerationDiagnostic> Diagnostics)
{
  public static SkeletonGenerationResult Success(GeneratedCardSkeleton skeleton) =>
    new(true, skeleton, []);

  public static SkeletonGenerationResult Failure(
    IReadOnlyList<SkeletonGenerationDiagnostic> diagnostics) =>
    new(false, null, diagnostics);
}
