namespace Game.Domain.Pooling;

public sealed record PoolResolutionResult(
  bool IsAccepted,
  string PoolId,
  IReadOnlyList<PoolItemRef> Candidates,
  IReadOnlyList<PoolResolutionError> Errors)
{
  public static PoolResolutionResult Accepted(
    string poolId,
    IReadOnlyList<PoolItemRef> candidates)
  {
    return new PoolResolutionResult(true, poolId, candidates, []);
  }

  public static PoolResolutionResult Rejected(
    string poolId,
    params PoolResolutionError[] errors)
  {
    return new PoolResolutionResult(false, poolId, [], errors);
  }
}
